(() => {
    const panel = document.querySelector('[data-member-balances]');
    let loading = false;
    async function refreshBalances() {
        if (!panel || document.hidden || loading) return;
        loading = true;
        try {
            const response = await fetch(panel.dataset.memberBalances, { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Refresh unavailable');
            const values = await response.json();
            document.querySelectorAll('[data-balance]').forEach(element => {
                const key = element.dataset.balance;
                const value = Number(values[key]);
                if (!Number.isFinite(value)) return;
                element.textContent = key === 'balance' ? `RM ${value.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` : value.toLocaleString('en-MY');
            });
            document.querySelector('[data-balance-status]').textContent = 'Balances are up to date.';
        } catch {
            document.querySelector('[data-balance-status]').textContent = 'Could not refresh balances. Your last loaded balances are shown.';
        } finally { loading = false; }
    }
    const qrSources = new WeakMap();
    const qrRequests = new WeakSet();
    const qrStatuses = new WeakMap();
    async function refreshQrImage(image) {
        if (qrRequests.has(image)) return;
        qrRequests.add(image);
        let status = qrStatuses.get(image);
        if (!status) {
            status = document.createElement('span');
            status.className = 'qr-status';
            status.setAttribute('role', 'status');
            status.hidden = true;
            image.insertAdjacentElement('afterend', status);
            qrStatuses.set(image, status);
        }
        const source = new URL(qrSources.get(image) || image.src, location.href);
        qrSources.set(image, source.toString());
        source.searchParams.set('format', 'json');
        source.searchParams.set('refresh', Date.now());
        try {
            const response = await fetch(source, { cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('QR unavailable');
            const qr = await response.json();
            // Use the same library and options as the Ionic member app.
            image.src = await EduvoQRCode.toDataURL(qr.payload, {
                width: qr.width, margin: 4, errorCorrectionLevel: qr.errorCorrectionLevel,
                color: { dark: '#141615', light: '#ffffff' }
            });
            image.hidden = false;
            status.hidden = true;
        } catch {
            image.hidden = true;
            status.textContent = 'Unable to refresh your secure QR. Reload this page to try again.';
            status.hidden = false;
        } finally { qrRequests.delete(image); }
    }
    function refreshQrImages() {
        if (document.hidden) return;
        document.querySelectorAll('img.member-qr, img.reward-qr').forEach(image => {
            refreshQrImage(image);
        });
    }
    refreshQrImages();
    if (panel) setInterval(refreshBalances, 10000);
    setInterval(refreshQrImages, 225000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { refreshBalances(); refreshQrImages(); } });
    window.addEventListener('pageshow', event => { if (event.persisted) { refreshBalances(); refreshQrImages(); } });
})();
