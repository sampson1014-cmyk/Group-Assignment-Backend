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
    function refreshQrImages() {
        if (document.hidden) return;
        document.querySelectorAll('img.member-qr, img.reward-qr').forEach(image => {
            const source = new URL(image.src, location.href);
            source.searchParams.set('refresh', Date.now());
            image.src = source.toString();
        });
    }
    if (panel) setInterval(refreshBalances, 10000);
    setInterval(refreshQrImages, 225000);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) { refreshBalances(); refreshQrImages(); } });
    window.addEventListener('pageshow', event => { if (event.persisted) { refreshBalances(); refreshQrImages(); } });
})();
