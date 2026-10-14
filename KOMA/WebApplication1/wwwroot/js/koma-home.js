(() => {
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const reveals = document.querySelectorAll('.reveal');
    if ('IntersectionObserver' in window && !reducedMotion.matches) {
        const observer = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.classList.remove('reveal-pending');
                    observer.unobserve(entry.target);
                }
            });
        }, { threshold: 0.08 });
        reveals.forEach(element => { element.classList.add('reveal-pending'); observer.observe(element); });
        reducedMotion.addEventListener('change', event => {
            if (event.matches) { reveals.forEach(element => element.classList.remove('reveal-pending')); observer.disconnect(); }
        });
    }
    const track = document.querySelector('.food-track');
    if (!track) return;
    const arrows = document.querySelectorAll('[data-direction]');
    const updateArrows = () => arrows.forEach(button => {
        button.disabled = Number(button.dataset.direction) < 0 ? track.scrollLeft <= 2 : track.scrollLeft >= track.scrollWidth - track.clientWidth - 2;
    });
    document.querySelectorAll('[data-filter]').forEach(button => button.addEventListener('click', () => {
        document.querySelectorAll('[data-filter]').forEach(filter => filter.setAttribute('aria-pressed', String(filter === button)));
        track.querySelectorAll('.food-card').forEach(card => { card.hidden = button.dataset.filter !== 'all' && !card.dataset.category.split(' ').includes(button.dataset.filter); });
        const empty = document.querySelector('.food-empty');
        empty.hidden = Array.from(track.querySelectorAll('.food-card')).some(card => !card.hidden);
        empty.textContent = 'No dishes in this selection yet. Check back soon.';
        track.scrollTo({ left: 0, behavior: 'instant' });
        updateArrows();
    }));
    arrows.forEach(button => button.addEventListener('click', () => {
        track.scrollBy({ left: Number(button.dataset.direction) * track.clientWidth * 0.85, behavior: reducedMotion.matches ? 'instant' : 'smooth' });
    }));
    track.addEventListener('scroll', updateArrows, { passive: true });
    window.addEventListener('resize', updateArrows);
    updateArrows();
    document.querySelectorAll('[data-order-message]').forEach(button => button.addEventListener('click', () => {
        document.querySelector('.order-status').textContent = button.dataset.orderMessage;
    }));
})();

