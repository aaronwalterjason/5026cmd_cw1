document.querySelectorAll('[data-feedback-open]').forEach(button => {
    button.addEventListener('click', () => document.getElementById(button.dataset.feedbackOpen).showModal());
});
document.querySelectorAll('[data-feedback-close]').forEach(button => {
    button.addEventListener('click', () => button.closest('dialog').close());
});
