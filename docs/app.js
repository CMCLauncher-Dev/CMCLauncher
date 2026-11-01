const toggle = document.querySelector('.menu-toggle');
const navigation = document.querySelector('#main-nav');

function closeMenu() {
  toggle.setAttribute('aria-expanded', 'false');
  navigation.classList.remove('is-open');
}

toggle.addEventListener('click', () => {
  const expanded = toggle.getAttribute('aria-expanded') !== 'true';
  toggle.setAttribute('aria-expanded', String(expanded));
  navigation.classList.toggle('is-open', expanded);
});
navigation.addEventListener('click', (event) => {
  if (event.target.closest('a')) closeMenu();
});
document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && toggle.getAttribute('aria-expanded') === 'true') {
    closeMenu();
    toggle.focus();
  }
});
window.matchMedia('(min-width: 721px)').addEventListener('change', closeMenu);
