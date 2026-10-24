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
      visible: true,
      autoShapes: config.shapes || [],
      // Engine arrows get brushes of their own so their opacity is set apart from arrows drawn by hand
      brushes: {
        engineBest: { key: 'eb', color: '#15781B', opacity: 1, lineWidth: 10 },
        engineAlt: { key: 'ea', color: '#003088', opacity: 0.4, lineWidth: 15 }
      }
    }
  };

  const cg = Chessground(el, cgConfig);
  boards.set(elementId, { cg, dotNetHelper });
  if (config.arrowOpacity) setArrowOpacity(elementId, config.arrowOpacity);
}

// Opacities 0-1: engineBest, engineAlt, drawn (the right-click brushes)
export function setArrowOpacity(elementId, opacity) {
  const instance = boards.get(elementId);
  if (!instance) return;

  const brushes = instance.cg.state.drawable.brushes;
  brushes.engineBest.opacity = opacity.engineBest;
  brushes.engineAlt.opacity = opacity.engineAlt;
  for (const name of ['green', 'red', 'blue', 'yellow']) {
    brushes[name].opacity = opacity.drawn;
  }

  // Shapes are only redrawn when they change, so force it
  instance.cg.state.drawable.prevSvgHash = '';
  instance.cg.redrawAll();
}

export function updateBoard(elementId, fen, orientation, turnColor, dests, lastMove, check, shapes) {
  const instance = boards.get(elementId);
  if (!instance) return;

  const destsMap = convertDests(dests);
  const formattedLastMove = lastMove && lastMove.length === 2 ? lastMove : undefined;

  const patch = {
    fen: fen,
    orientation: orientation || 'white',
    turnColor: turnColor || 'white',
    check: check || false,
    lastMove: formattedLastMove,
    movable: {
      color: turnColor || 'white',
      dests: destsMap
    }
  };

  if (shapes !== undefined && shapes !== null) {
    patch.drawable = { autoShapes: shapes };
  }

  instance.cg.set(patch);
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
    if (instance.cg && instance.cg.state && instance.cg.state.dom) {
      instance.cg.state.dom.bounds.clear();
      instance.cg.state.dom.redrawNow();
    }
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
