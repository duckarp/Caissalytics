export function setEval(elementId, whitePct, scoreText) {
  const container = document.getElementById(elementId);
  if (!container) return;

  const whiteBar = container.querySelector('.eval-gauge-white-bar');
  const labelWhite = container.querySelector('.label-white-side');
  const labelBlack = container.querySelector('.label-black-side');

  if (whiteBar) {
    whiteBar.style.height = `${Math.max(0, Math.min(100, whitePct))}%`;
  }

  if (whitePct >= 50) {
    if (labelWhite) labelWhite.textContent = scoreText;
    if (labelBlack) labelBlack.textContent = '';
  } else {
    if (labelBlack) labelBlack.textContent = scoreText;
    if (labelWhite) labelWhite.textContent = '';
  }
}
