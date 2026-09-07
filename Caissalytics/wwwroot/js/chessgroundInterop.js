import { Chessground } from '../lib/chessground/chessground.min.js';

const boards = new Map();

function convertDests(destsObj) {
  if (!destsObj) return new Map();
  const destsMap = new Map();
  for (const [orig, dests] of Object.entries(destsObj)) {
    if (dests && dests.length > 0) {
      destsMap.set(orig, dests);
    }
  }
  return destsMap;
}

export function initBoard(elementId, dotNetHelper, config) {
  const el = document.getElementById(elementId);
  if (!el) {
    console.error(`Chessground target element #${elementId} not found.`);
    return;
  }

  // Clear existing if any
  if (boards.has(elementId)) {
    destroyBoard(elementId);
  }

  const destsMap = convertDests(config.dests);

  const cgConfig = {
    fen: config.fen || 'start',
    orientation: config.orientation || 'white',
    turnColor: config.turnColor || 'white',
    coordinates: true,
    autoCastle: true,
    viewOnly: config.viewOnly || false,
    highlight: {
      lastMove: true,
      check: true
    },
    animation: {
      enabled: true,
      duration: 200
    },
    movable: {
      free: false,
      color: config.turnColor || 'white',
      dests: destsMap,
      events: {
        after: (orig, dest, metadata) => {
          dotNetHelper.invokeMethodAsync('OnPieceMoved', orig, dest);
        }
      }
    },
    drawable: {
      enabled: true,
      visible: true
    }
  };

  const cg = Chessground(el, cgConfig);
  boards.set(elementId, { cg, dotNetHelper });
}

export function updateBoard(elementId, fen, orientation, turnColor, dests, lastMove, check) {
  const instance = boards.get(elementId);
  if (!instance) return;

  const destsMap = convertDests(dests);
  const formattedLastMove = lastMove && lastMove.length === 2 ? lastMove : undefined;

  instance.cg.set({
    fen: fen,
    orientation: orientation || 'white',
    turnColor: turnColor || 'white',
    check: check || false,
    lastMove: formattedLastMove,
    movable: {
      color: turnColor || 'white',
      dests: destsMap
    }
  });
}

export function drawShapes(elementId, shapes) {
  const instance = boards.get(elementId);
  if (!instance) return;

  instance.cg.setAutoShapes(shapes || []);
}

export function redrawBoard(elementId) {
  const instance = boards.get(elementId);
  if (!instance) return;
  window.requestAnimationFrame(() => {
    window.dispatchEvent(new Event('resize'));
  });
}

export function destroyBoard(elementId) {
  const instance = boards.get(elementId);
  if (instance) {
    if (instance.cg && instance.cg.destroy) {
      instance.cg.destroy();
    }
    boards.delete(elementId);
  }
}
