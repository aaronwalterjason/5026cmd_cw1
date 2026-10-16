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
    document.querySelectorAll('.food-gallery').forEach(gallery => {
        const track = gallery.querySelector('.food-track');
        const cards = Array.from(track.querySelectorAll('.food-card'));
        let hovered = false, focused = false, timer;
        let animationFrame;
        let restoreSnap;
        const cancelSlide = () => {
            window.cancelAnimationFrame(animationFrame);
            if (restoreSnap !== undefined) {
                track.style.scrollSnapType = restoreSnap;
                restoreSnap = undefined;
            }
        };
        const animateSlide = target => {
            cancelSlide();
            if (reducedMotion.matches) {
                track.scrollTo({ left: target, behavior: 'instant' });
                return;
            }
            const start = track.scrollLeft;
            const startedAt = performance.now();
            const duration = 1200;
            restoreSnap = track.style.scrollSnapType;
            track.style.scrollSnapType = 'none';
            const tick = now => {
                const progress = Math.min((now - startedAt) / duration, 1);
                const eased = (1 - Math.cos(Math.PI * progress)) / 2;
                track.scrollTo({ left: start + (target - start) * eased, behavior: 'instant' });
                if (progress < 1) animationFrame = window.requestAnimationFrame(tick);
                else cancelSlide();
            };
            animationFrame = window.requestAnimationFrame(tick);
        };
        track.addEventListener('pointerdown', cancelSlide);
        track.addEventListener('wheel', cancelSlide, { passive: true });
        reducedMotion.addEventListener('change', cancelSlide);
        const move = direction => {
            const max = track.scrollWidth - track.clientWidth;
            if (max <= 2) return;
            const step = cards.length > 1 ? cards[1].offsetLeft - cards[0].offsetLeft : track.clientWidth;
            let next = track.scrollLeft + direction * step;
            if (direction > 0 && track.scrollLeft >= max - 2) next = 0;
            if (direction < 0 && track.scrollLeft <= 2) next = max;
            animateSlide(Math.max(0, Math.min(max, next)));
        };
        const restart = () => {
            window.clearInterval(timer);
            if (cards.length > 1 && !hovered && !focused && !document.hidden && !reducedMotion.matches) timer = window.setInterval(() => move(1), 5000);
        };
        gallery.addEventListener('mouseenter', () => { hovered = true; restart(); });
        gallery.addEventListener('mouseleave', () => { hovered = false; restart(); });
        gallery.addEventListener('focusin', () => { focused = true; restart(); });
        gallery.addEventListener('focusout', event => { focused = gallery.contains(event.relatedTarget); restart(); });
        track.addEventListener('keydown', event => {
            if (event.target !== track || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
            event.preventDefault();
            move(event.key === 'ArrowLeft' ? -1 : 1);
        });
        document.addEventListener('visibilitychange', restart);
        reducedMotion.addEventListener('change', restart);
        restart();
    });
    document.querySelectorAll('[data-order-message]').forEach(button => button.addEventListener('click', () => {
        document.querySelector('.order-status').textContent = button.dataset.orderMessage;
    }));
})();

