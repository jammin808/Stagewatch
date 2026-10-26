#!/usr/bin/env python3
"""A one-kernel LAN for Stagewatch's interop lane, made with rtnetlink (stdlib only; Python 3.12 or later for os.unshare and
os.setns). It runs inside the lane's own user, network and PID namespaces (tools/test.sh interop) and changes nothing on the
machine's own network or settings: every link, address and namespace it makes is gone when those namespaces end. It builds a
topology only in a network namespace made for the run, which it checks before it makes anything: one that is fresh (a
loopback that is still down, no other link and no route, as unshare leaves a new one) inside a user namespace of its own. The
machine's own network, a container's (its own links, or a loopback already up) and one it has built in before (br0 and its
ports are still there) are each refused with exit 2 and the reason, so no link it makes outlives the namespace made for it.

  lan.py lan <host> ...   the switch: a bridge br0 at 10.99.0.1/24 in this namespace (the driver's) and a network namespace
                          of its own per host, joined to br0 by a veth (eth0 inside, sw-<host> on the bridge) at
                          10.99.0.<n>/24, n = 2, 3, ... in the order named, with its own loopback and a default route: a
                          broadcast crosses the bridge as on a real LAN and every host dials from its own address
  lan.py laptop           one host, this namespace (the show laptop): a veth wifi0 at 10.99.0.2/24 with a default route, its
                          far end the access point at 10.99.0.1 in a namespace of its own
  lan.py offline          one host, this namespace, loopback only
  lan.py probe            whether this kernel gives a fresh namespace a bridge and a veth: one line, exit 0 or 3; nothing kept

Standard output, line 1: {"topology":"lan","switch":"10.99.0.1","hosts":{"rig":{"addr":"10.99.0.2","ns":"/proc/<pid>/ns/net"},
...}}: a host's ns is the network namespace to run its app in (nsenter --net=<ns>). Then one JSON line for each line read on
standard input: "down <host>" / "up <host>" pull or put back that host's cable, its far end, so the host keeps its own
addresses and routes as a machine whose cable is pulled does ({"cable":"<host>","state":"down"}); anything else is refused
({"error":"unknown (down <host>, up <host>)"}). The end of standard input ends this process, and the namespaces' holders end
with it: they wait on a pipe only this process holds open, never on standard input.
Exit codes: 0 the input ended; 2 a command line it cannot take, or a namespace it will not build in (above); 3 the kernel
refused (no namespaces, no bridge or veth for an unprivileged namespace, no right to make links): its words are standard
output's one line, {"error":"..."}.
"""
import errno
import fcntl
import json
import os
import re
import socket
import struct
import sys

RTM_NEWLINK, RTM_NEWADDR, RTM_NEWROUTE = 16, 20, 24
NLM_F_REQUEST, NLM_F_ACK, NLM_F_EXCL, NLM_F_CREATE = 0x1, 0x4, 0x200, 0x400
NLMSG_ERROR = 2
IFLA_IFNAME, IFLA_MASTER, IFLA_LINKINFO, IFLA_NET_NS_PID = 3, 10, 18, 19
IFLA_INFO_KIND, IFLA_INFO_DATA, VETH_INFO_PEER = 1, 2, 1
IFA_ADDRESS, IFA_LOCAL, IFA_BROADCAST = 1, 2, 4
RTA_OIF = 4
RT_TABLE_MAIN, RTPROT_BOOT, RT_SCOPE_LINK, RTN_UNICAST = 254, 3, 253, 1
IFF_UP = 0x1
SIOCGIFFLAGS = 0x8913

SUBNET = '10.99.0.'
SWITCH = SUBNET + '1'
BROADCAST = SUBNET + '255'
MAX_HOSTS = 200                                   # .2 to .201: the /24 has room, and nothing needs more
HOST_NAME = re.compile(r'^[a-z][a-z0-9-]{0,11}$')  # sw-<host> stays within a link name's 15 characters


class Refused(Exception):
    """The kernel would not make what the topology needs: said as the one {"error"} line, exit 3."""


def attr(kind, data):
    size = 4 + len(data)
    return struct.pack('=HH', size, kind) + data + b'\0' * ((4 - size % 4) % 4)


def name_attr(name):
    return attr(IFLA_IFNAME, name.encode() + b'\0')


def ifinfo(index=0, flags=0, change=0):
    return struct.pack('=BxHiII', socket.AF_UNSPEC, 0, index, flags, change)


class Netlink:
    """One rtnetlink socket, bound to the network namespace this thread was in when it was made."""

    def __init__(self):
        self.sock = socket.socket(socket.AF_NETLINK, socket.SOCK_RAW, 0)
        self.sock.bind((0, 0))
        self.seq = 0

    def request(self, kind, body, flags=NLM_F_REQUEST | NLM_F_ACK):
        self.seq += 1
        self.sock.send(struct.pack('=IHHII', 16 + len(body), kind, flags, self.seq, 0) + body)
        while True:                                # every request asks for an ACK: its error message ends the wait
            data = self.sock.recv(65536)
            at = 0
            while at + 16 <= len(data):
                size, mtype, _, seq, _ = struct.unpack_from('=IHHII', data, at)
                if mtype == NLMSG_ERROR and seq == self.seq:
                    code = struct.unpack_from('=i', data, at + 16)[0]
                    if code:
                        raise OSError(-code, os.strerror(-code))
                    return
                if size < 16:
                    break
                at += (size + 3) & ~3

    def create(self, body):
        self.request(RTM_NEWLINK, body, NLM_F_REQUEST | NLM_F_ACK | NLM_F_CREATE | NLM_F_EXCL)

    def bridge(self, name):
        self.create(ifinfo() + name_attr(name) + attr(IFLA_LINKINFO, attr(IFLA_INFO_KIND, b'bridge')))

    def veth(self, name, peer, peer_pid=None):
        far = ifinfo() + name_attr(peer) + (attr(IFLA_NET_NS_PID, struct.pack('=I', peer_pid)) if peer_pid else b'')
        self.create(ifinfo() + name_attr(name)
                    + attr(IFLA_LINKINFO, attr(IFLA_INFO_KIND, b'veth') + attr(IFLA_INFO_DATA, attr(VETH_INFO_PEER, far))))

    def set_link(self, name, up=None, master=None):
        """A link changed by name (no index, so the name is looked up in this socket's own namespace)."""
        flags, change = (IFF_UP if up else 0, IFF_UP) if up is not None else (0, 0)
        body = ifinfo(0, flags, change) + name_attr(name)
        if master is not None:
            body += attr(IFLA_MASTER, struct.pack('=I', socket.if_nametoindex(master)))
        self.request(RTM_NEWLINK, body)

    def address(self, name, addr, prefix=24):
        index = socket.if_nametoindex(name)
        local = socket.inet_aton(addr)
        self.request(RTM_NEWADDR, struct.pack('=BBBBI', socket.AF_INET, prefix, 0, 0, index)
                     + attr(IFA_LOCAL, local) + attr(IFA_ADDRESS, local) + attr(IFA_BROADCAST, socket.inet_aton(BROADCAST)),
                     NLM_F_REQUEST | NLM_F_ACK | NLM_F_CREATE | NLM_F_EXCL)

    def default_route(self, name):
        """Everything off this host goes out of `name` as on-link (no gateway): 255.255.255.255 then has a way out."""
        self.request(RTM_NEWROUTE, struct.pack('=BBBBBBBBI', socket.AF_INET, 0, 0, 0, RT_TABLE_MAIN, RTPROT_BOOT,
                                                RT_SCOPE_LINK, RTN_UNICAST, 0)
                     + attr(RTA_OIF, struct.pack('=i', socket.if_nametoindex(name))),
                     NLM_F_REQUEST | NLM_F_ACK | NLM_F_CREATE | NLM_F_EXCL)

    def close(self):
        self.sock.close()


def words(err, what):
    """The kernel's refusal in words that say what to do about it."""
    if err.errno == errno.EOPNOTSUPP:
        return (f'this kernel makes no {what} for an unprivileged namespace: its module is not loaded '
                '(on a runner, sudo modprobe -a bridge veth before the lane)')
    if err.errno == errno.EPERM:
        return f'no right to make a {what} here: lan.py runs inside the lane\'s namespaces (tools/test.sh interop)'
    return f'the kernel refused a {what}: {err.strerror}'


def made(what, step):
    try:
        return step()
    except OSError as err:
        raise Refused(words(err, what)) from err


def enter(path):
    fd = os.open(path, os.O_RDONLY)
    try:
        os.setns(fd, os.CLONE_NEWNET)
    finally:
        os.close(fd)


class Holders:
    """The network namespaces' holders: one forked process per namespace, waiting on a pipe whose only writer is this
    process, so each ends when this process ends, whatever ends it."""

    def __init__(self):
        self.read_end, self.write_end = os.pipe()

    def new(self):
        ready_r, ready_w = os.pipe()
        pid = os.fork()
        if pid == 0:
            try:
                os.close(ready_r)
                os.close(self.write_end)
                quiet = os.open(os.devnull, os.O_RDWR)
                for fd in (0, 1, 2):                # no holder keeps the driver's pipes open
                    os.dup2(quiet, fd)
                os.unshare(os.CLONE_NEWNET)
                os.write(ready_w, b'1')
                os.close(ready_w)
                os.read(self.read_end, 1)           # the end of the pipe: this process has ended
            finally:
                os._exit(0)
        os.close(ready_w)
        said = os.read(ready_r, 1)
        os.close(ready_r)
        if said != b'1':
            raise Refused('a namespace holder could not make a network namespace of its own')
        return pid

    def done(self):
        os.close(self.read_end)                     # the holders have their own copies; this process keeps the writer


def in_namespace(pid, setup):
    """Run setup(netlink) inside pid's network namespace, then come back; the socket it is given stays bound there."""
    home = os.open('/proc/self/ns/net', os.O_RDONLY)
    try:
        enter(f'/proc/{pid}/ns/net')
        nl = Netlink()
        setup(nl)
        return nl
    finally:
        os.setns(home, os.CLONE_NEWNET)
        os.close(home)


def lan(names):
    """The switch in this namespace and one host namespace per name; returns (map, cables) where cables[name] is the netlink
    socket and the link name that pull that host's cable."""
    nl = Netlink()
    made('loopback', lambda: nl.set_link('lo', up=True))
    made('bridge', lambda: nl.bridge('br0'))
    made('bridge', lambda: nl.set_link('br0', up=True))
    made('bridge address', lambda: nl.address('br0', SWITCH))
    made('route', lambda: nl.default_route('br0'))
    holders = Holders()
    hosts, cables = {}, {}
    for i, name in enumerate(names):
        pid = holders.new()
        addr = SUBNET + str(i + 2)
        port = 'sw-' + name
        made('veth', lambda: nl.veth(port, 'eth0', pid))
        made('bridge port', lambda: nl.set_link(port, master='br0'))
        made('bridge port', lambda: nl.set_link(port, up=True))

        def host_side(inner, addr=addr):
            made('loopback', lambda: inner.set_link('lo', up=True))
            made('veth', lambda: inner.set_link('eth0', up=True))
            made('address', lambda: inner.address('eth0', addr))
            made('route', lambda: inner.default_route('eth0'))
        in_namespace(pid, host_side).close()
        hosts[name] = {'addr': addr, 'ns': f'/proc/{pid}/ns/net'}
        cables[name] = (nl, port)
    holders.done()
    return {'topology': 'lan', 'switch': SWITCH, 'hosts': hosts}, cables


def laptop():
    """This namespace is the laptop on its Wi-Fi; the access point's end of the veth is its cable."""
    nl = Netlink()
    made('loopback', lambda: nl.set_link('lo', up=True))
    holders = Holders()
    ap = holders.new()
    made('veth', lambda: nl.veth('wifi0', 'ap0', ap))
    made('veth', lambda: nl.set_link('wifi0', up=True))
    made('address', lambda: nl.address('wifi0', SUBNET + '2'))
    made('route', lambda: nl.default_route('wifi0'))

    def access_point(inner):
        made('loopback', lambda: inner.set_link('lo', up=True))
        made('veth', lambda: inner.set_link('ap0', up=True))
        made('address', lambda: inner.address('ap0', SWITCH))
    ap_nl = in_namespace(ap, access_point)
    holders.done()
    here = f'/proc/{os.getpid()}/ns/net'
    return {'topology': 'laptop', 'switch': SWITCH, 'hosts': {'laptop': {'addr': SUBNET + '2', 'ns': here}}}, \
        {'laptop': (ap_nl, 'ap0')}


def offline():
    nl = Netlink()
    made('loopback', lambda: nl.set_link('lo', up=True))
    here = f'/proc/{os.getpid()}/ns/net'
    return {'topology': 'offline', 'switch': '', 'hosts': {'laptop': {'addr': '127.0.0.1', 'ns': here}}}, {}


def probe():
    """In a namespace of this process's own, the two kinds of link the LAN is made of; nothing outlives this process."""
    try:
        os.unshare(os.CLONE_NEWNET)
    except OSError as err:
        raise Refused('this kernel gives no network namespace here (user namespaces off, or a restriction on '
                      f'unprivileged ones, such as AppArmor\'s apparmor_restrict_unprivileged_userns): {err.strerror}') from err
    nl = Netlink()
    made('bridge', lambda: nl.bridge('br0'))
    made('veth', lambda: nl.veth('p0', 'p1'))
    made('bridge port', lambda: nl.set_link('p0', master='br0'))
    return {'probe': 'ok'}


def in_own_user_namespace():
    """Whether this process is in a user namespace other than the machine's first one: there, no link it makes can reach
    the machine's own network namespace. The second of the two checks a topology is built behind (not_fresh is the first)."""
    try:
        with open('/proc/self/uid_map', encoding='ascii') as f:
            rows = [row.split() for row in f.read().splitlines() if row.strip()]
    except OSError:
        return False
    return rows != [['0', '0', '4294967295']]


def not_fresh():
    """What makes this network namespace other than a fresh one, in words; empty when it is fresh. A namespace unshare has
    just made holds one link, its loopback, still down, and no route; the machine's own, a container's and one a topology was
    built in before each hold more (an adapter, a loopback already up, br0 or a host's port, a route), and a topology built
    there would outlive the run."""
    found = []
    others = sorted(name for _, name in socket.if_nameindex() if name != 'lo')
    if others:
        found.append(('the link ' if len(others) == 1 else 'the links ') + ', '.join(others))
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as s:
            asked = fcntl.ioctl(s.fileno(), SIOCGIFFLAGS, struct.pack('=16sH22x', b'lo', 0))
        if struct.unpack_from('=H', asked, 16)[0] & IFF_UP:
            found.append('a loopback already up')
    except OSError as err:
        found.append(f'a loopback it cannot read ({err.strerror})')
    try:
        with open('/proc/net/route', encoding='ascii') as f:      # this namespace's IPv4 routes, after a header row
            routes = len([row for row in f.read().splitlines()[1:] if row.strip()])
        if routes:
            found.append(f'{routes} route{"s" if routes != 1 else ""}')
    except OSError as err:
        found.append(f'a route table it cannot read ({err.strerror})')
    return found


def say(obj):
    sys.stdout.write(json.dumps(obj) + '\n')
    sys.stdout.flush()


def serve(cables):
    for line in sys.stdin:
        word, _, name = line.strip().partition(' ')
        if word in ('down', 'up') and name in cables:
            nl, port = cables[name]
            try:
                nl.set_link(port, up=word == 'up')
                say({'cable': name, 'state': word})
            except OSError as err:
                say({'error': f'the cable of {name} did not go {word}: {err.strerror}'})
        elif word in ('down', 'up') and not cables:
            say({'error': 'this topology has no cable'})
        else:
            say({'error': 'unknown (down <host>, up <host>)'})


def main(argv):
    if sys.version_info < (3, 12):
        say({'error': f'lan.py needs Python 3.12 or later (os.unshare, os.setns); this is {sys.version.split()[0]}'})
        return 2
    if len(argv) < 2 or argv[1] not in ('lan', 'laptop', 'offline', 'probe'):
        say({'error': 'usage: lan.py lan <host> ... | laptop | offline | probe'})
        return 2
    topology, names = argv[1], argv[2:]
    if topology == 'lan':
        if not names or len(names) > MAX_HOSTS or len(set(names)) != len(names) \
                or not all(HOST_NAME.match(n) for n in names):
            say({'error': f'lan takes 1 to {MAX_HOSTS} distinct host names, each a lower-case letter then up to 11 '
                          'letters, digits or -'})
            return 2
    elif names:
        say({'error': f'{topology} takes no host names'})
        return 2
    if topology != 'probe':
        held = not_fresh()
        if held:
            say({'error': 'lan.py builds a topology only in a fresh network namespace made for the run (a loopback still '
                          'down, no other link, no route), as tools/test.sh interop makes one, and this one has '
                          + '; '.join(held) + ': never the machine\'s own network, a container\'s, or one built in before'})
            return 2
        if not in_own_user_namespace():
            say({'error': 'lan.py builds a topology only inside a user namespace of its own (tools/test.sh interop starts '
                          'it in one), never on the machine\'s own network'})
            return 2
    try:
        if topology == 'probe':
            say(probe())
            return 0
        lan_map, cables = lan(names) if topology == 'lan' else laptop() if topology == 'laptop' else offline()
    except Refused as refused:
        say({'error': str(refused)})
        return 3
    say(lan_map)
    serve(cables)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
