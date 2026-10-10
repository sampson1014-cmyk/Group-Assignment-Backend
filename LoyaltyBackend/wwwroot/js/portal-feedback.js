(() => {
    if (!window.Swal) return; // Keep server-rendered messages and native confirmations available.
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const dialogs = Swal.mixin({
        heightAuto: false,
        buttonsStyling: false,
        customClass: {
            popup: 'portal-dialog', title: 'portal-dialog-title',
            htmlContainer: 'portal-dialog-copy',
            confirmButton: 'btn btn-primary', cancelButton: 'btn btn-outline-dark',
            actions: 'portal-dialog-actions'
        },
        ...(reducedMotion ? { showClass: { popup: '' }, hideClass: { popup: '' } } : {})
    });
    function toast(message, icon = 'success') {
        return dialogs.fire({
            toast: true, position: 'top-end', titleText: message, icon,
            iconColor: icon === 'error' ? '#b42336' : '#5b5bd6',
            showConfirmButton: false, showCloseButton: true,
            closeButtonAriaLabel: 'Dismiss message',
            timer: icon === 'error' ? undefined : 6500,
            timerProgressBar: icon !== 'error',
            customClass: { popup: 'portal-toast', title: 'portal-toast-title' },
            didOpen: popup => {
                popup.addEventListener('mouseenter', Swal.stopTimer);
                popup.addEventListener('mouseleave', Swal.resumeTimer);
                popup.addEventListener('focusin', Swal.stopTimer);
                popup.addEventListener('focusout', Swal.resumeTimer);
            }
        });
    }
    window.EduvoFeedback = { toast };
    // Server messages are Razor-encoded and passed as plain text, never HTML.
    const messages = [...document.querySelectorAll('[data-portal-flash]')];
    (async () => {
        for (const element of messages) {
            const pending = toast(element.textContent.trim(), element.dataset.portalFlash);
            element.hidden = true;
            await pending;
        }
    })();
    const approved = new WeakSet();
    const pending = new WeakSet();
    document.querySelectorAll('form[data-confirm-title]').forEach(form => {
        form.addEventListener('submit', async event => {
            if (approved.has(form)) { approved.delete(form); return; }
            event.preventDefault();
            if (pending.has(form)) return;
            if (!form.reportValidity() || (window.jQuery?.validator && !window.jQuery(form).valid())) return;
            pending.add(form);
            try {
                const result = await dialogs.fire({
                    titleText: form.dataset.confirmTitle, text: form.dataset.confirmMessage,
                    icon: 'question', iconColor: '#5b5bd6', showCancelButton: true,
                    confirmButtonText: form.dataset.confirmButton || 'Continue',
                    cancelButtonText: 'Cancel', focusCancel: true
                });
                if (result.isConfirmed) {
                    approved.add(form);
                    form.requestSubmit(event.submitter || undefined);
                }
            } finally { pending.delete(form); }
        });
    });
})();
