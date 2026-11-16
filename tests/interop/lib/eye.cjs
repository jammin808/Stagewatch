// What Patterns' God's Eye draws of the show network, read from its EYE JSON over its paired wire (EYE answers a paired
// connection only, so a link's record reaches the lane by the same door a deck reads it). Patterns' contract (its docs/REMOTE.md
// at the lane's pin, `EYE`): each node row carries `app` (the app whose cluster holds it, "" for the desk's own), `source` (own,
// link, told, relayed or heard) and `collapsed`; each edge row `source`, `claim` (the app whose claim it is, "" for a fact) and
// `link` (the 13.16.3 record of a link the desk holds, the 13.16.5 row of a link its wire accepted, null otherwise); the root
// `apps`, `linkLights{green,amber,red,grey}`, `unplaced` and `left.edges`. These readers judge fields, never drawn words alone:
// a card is found by its id and kind, an edge by its ends and source, a thing by its id and its kind by the app's own context
// item of that id (never the thing's drawn words); words are read only where the contract puts a fact in them: an app's claim on
// a link this desk holds is the edge's words, as its record leads, and a counting thing's `sub` is what it shows in place of a
// count (the stuck words).
'use strict';

/** The desk's own node on Patterns' Eye (Eye.DeskId). */
const DESK = 'desk';
/** The "+N more" card (Eye.MoreId): no app's card. */
const MORE = 'apps:more';
/** A card of an app: an app's own (showapp) or another Patterns node's (node). */
const CARD_KINDS = ['showapp', 'node'];
/** The counting kinds (Patterns' ShowBusView.IsCounting): from an app whose show is stuck none is shown as current. */
const COUNTING = ['timer', 'schedule', 'playlist', 'chase', 'timecode', 'clock'];
/** What a counting thing of a stuck app says in place of its count (ShowBusView.NoTimeWords). */
const NO_TIME = '? — no time shown — its show is stuck';
/** What every reader says of a linked app whose show is stuck (ShowBusView.StuckWords). */
const STUCK_WORDS = 'says its show is stuck — no time shown';

/** A card's id for an app id (EyeNetwork.CardOf). */
const cardOf = (id) => (id.startsWith('node:') ? id : `app:${id}`);

/** Every card of the Eye that stands for app `id`: its own card and any other card or deck naming that id. */
function cardsFor(eye, id) {
  return (eye.nodes || []).filter((n) => n.id !== MORE && (CARD_KINDS.includes(n.kind) || n.kind === 'deck')
    && (n.app === id || n.id === cardOf(id) || n.id.endsWith(`:${id}`)));
}

/** The decks drawn from an address (Eye: `deck:<name>@<address>`). */
function decksFrom(eye, addr) {
  return (eye.nodes || []).filter((n) => n.kind === 'deck' && n.id.slice(n.id.lastIndexOf('@') + 1) === addr);
}

/** Every edge between nodes a and b, either way. */
function edgesBetween(eye, a, b) {
  return (eye.edges || []).filter((e) => (e.from === a && e.to === b) || (e.from === b && e.to === a));
}

/** The links this desk holds or accepts as EYE draws them (EyeEdge.IsLink: source link, never a twin node's carrier). */
function linkEdges(eye) {
  return (eye.edges || []).filter((e) => e.source === 'link' && !e.from.startsWith('node:') && !e.to.startsWith('node:'));
}

/**
 * The things of app `id` drawn on its cluster (`<card>:<item>`), each with the kind of the item of that id in `ctx`, the app's own
 * context read beside the Eye ("" for a thing the context has no item for).
 */
function thingsOf(eye, id, ctx) {
  const prefix = `${cardOf(id)}:`;
  const kinds = Object.fromEntries(((ctx && ctx.items) || []).map((i) => [i.id, i.kind]));
  return (eye.nodes || []).filter((n) => n.kind === 'showappitem' && n.app === id && n.id.startsWith(prefix)).map((n) => {
    const item = n.id.slice(prefix.length);
    return { id: n.id, item, kind: kinds[item] || '', light: n.light, sub: n.sub, source: n.source };
  });
}

/**
 * The one card of app `id` on the Eye and its one edge to the desk, as fields: exactly one card for the id (its own card, of a
 * card's kind, no deck and no second card for the same id, `decksAt` the addresses a deck from the app would be drawn from), and
 * exactly one edge between that card and the desk, whose source is `link` (a link this desk holds or accepts: Patterns'
 * EyeOrigin.Link) and which carries its link record. Returns { card, edge } or throws with what was read.
 */
function oneCardOneEdge(eye, id, decksAt) {
  const cards = cardsFor(eye, id);
  const decks = decksAt.flatMap((a) => decksFrom(eye, a));
  const wrong = [];
  if (cards.length !== 1 || cards[0].id !== cardOf(id) || !CARD_KINDS.includes(cards[0].kind)) {
    wrong.push(`${cards.length} cards for ${id}: ${JSON.stringify(cards.map((c) => ({ id: c.id, kind: c.kind, app: c.app, source: c.source })))}`);
  }
  if (decks.length) wrong.push(`decks drawn from ${decksAt.join(', ')}: ${JSON.stringify(decks.map((d) => d.id))}`);
  const card = cards.find((c) => c.id === cardOf(id));
  if (card && (card.app !== id || card.source !== 'link')) wrong.push(`its card's app "${card.app}", source "${card.source}", not ${id} by its link`);
  const edges = edgesBetween(eye, cardOf(id), DESK);
  if (edges.length !== 1) wrong.push(`${edges.length} edges between ${cardOf(id)} and the desk: ${JSON.stringify(edges.map((e) => ({ kind: e.kind, source: e.source, claim: e.claim })))}`);
  const edge = edges[0];
  if (edge && (edge.source !== 'link' || edge.claim !== '' || !edge.link)) wrong.push(`its edge's source "${edge.source}", claim "${edge.claim}", link ${edge.link ? 'given' : 'null'}`);
  if (wrong.length) throw new Error(`Patterns' Eye for ${id}: ${wrong.join('; ')}`);
  return { card, edge };
}

/** The node and item a §6 end names (`@<node>` or `@<node>/<item>`), or null for a word that is no end. */
function endOf(text) {
  const m = /^@([A-Za-z0-9_-]{6,40})(?:\/(.+))?$/.exec(text || '');
  return m ? { node: m[1], item: m[2] || '' } : null;
}

/** The rows of a context with an end at another node (§6), each { from, to, kind, light, end, local, outward }. */
function rowsWithEnds(ctx) {
  return (ctx.links || []).flatMap((l) => {
    const to = endOf(l.to);
    const from = endOf(l.from);
    if (!to && !from) return [];
    const outward = !!to;
    return [{ from: l.from, to: l.to, kind: l.kind, light: l.light, end: outward ? to : from, local: outward ? l.from : l.to, outward }];
  });
}

module.exports = {
  DESK, MORE, COUNTING, NO_TIME, STUCK_WORDS,
  cardOf, cardsFor, decksFrom, edgesBetween, linkEdges, thingsOf, oneCardOneEdge, endOf, rowsWithEnds,
};
