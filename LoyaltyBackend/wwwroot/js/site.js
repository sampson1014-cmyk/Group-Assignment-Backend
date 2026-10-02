const locationButton = document.querySelector("#locationButton");

locationButton?.addEventListener("click", () => {
    if (!navigator.geolocation) {
        locationButton.textContent = "Location is unavailable";
        return;
    }

    locationButton.disabled = true;
    locationButton.textContent = "Finding you…";
    navigator.geolocation.getCurrentPosition(position => {
        document.querySelectorAll(".outlet-card").forEach(card => {
            const distance = haversine(position.coords.latitude, position.coords.longitude,
                Number(card.dataset.lat), Number(card.dataset.lng));
            card.querySelector(".distance").textContent = `${distance.toFixed(1)} km away`;
        });
        locationButton.textContent = "Distances updated";
    }, () => {
        locationButton.disabled = false;
        locationButton.textContent = "Location permission denied";
    });
});

function haversine(lat1, lon1, lat2, lon2) {
    const radians = value => value * Math.PI / 180;
    const dLat = radians(lat2 - lat1);
    const dLon = radians(lon2 - lon1);
    const a = Math.sin(dLat / 2) ** 2 + Math.cos(radians(lat1)) * Math.cos(radians(lat2)) * Math.sin(dLon / 2) ** 2;
    return 6371 * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

const outletMap = document.querySelector("#outletMap");
const outletCards = [...document.querySelectorAll(".outlet-card")];
const openMapLink = document.querySelector("#openMapLink");

function showOutletOnMap(card) {
    const lat = Number(card.dataset.lat);
    const lng = Number(card.dataset.lng);
    if (!outletMap || !Number.isFinite(lat) || !Number.isFinite(lng) || (lat === 0 && lng === 0)) return;
    const span = 0.018;
    const bbox = [lng - span, lat - span, lng + span, lat + span].join(",");
    outletMap.src = `https://www.openstreetmap.org/export/embed.html?bbox=${encodeURIComponent(bbox)}&layer=mapnik&marker=${encodeURIComponent(`${lat},${lng}`)}`;
    if (openMapLink) openMapLink.href = `https://www.openstreetmap.org/?mlat=${encodeURIComponent(lat)}&mlon=${encodeURIComponent(lng)}#map=16/${encodeURIComponent(lat)}/${encodeURIComponent(lng)}`;
    outletCards.forEach(item => item.classList.toggle("selected", item === card));
}

outletCards.forEach(card => card.querySelector(".outlet-map-button")?.addEventListener("click", () => showOutletOnMap(card)));
const firstMappableOutlet = outletCards.find(card => Number(card.dataset.lat) !== 0 || Number(card.dataset.lng) !== 0);
if (firstMappableOutlet) showOutletOnMap(firstMappableOutlet);

document.querySelectorAll("[data-reward-tab]").forEach(button => {
    button.addEventListener("click", () => {
        document.querySelectorAll("[data-reward-tab]").forEach(item => item.classList.remove("active"));
        document.querySelectorAll("[data-reward-panel]").forEach(item => item.classList.add("d-none"));
        button.classList.add("active");
        document.querySelector(`[data-reward-panel="${button.dataset.rewardTab}"]`)?.classList.remove("d-none");
    });
});

document.querySelector("#copyReferral")?.addEventListener("click", async event => {
    const code = document.querySelector("#referralCode")?.textContent?.trim();
    if (!code) return;
    await navigator.clipboard.writeText(code);
    event.currentTarget.textContent = "Copied";
});

document.querySelector("#shareReferral")?.addEventListener("click", async event => {
    const code = document.querySelector("#referralCode")?.textContent?.trim();
    if (!code) return;
    const text = `Join Eduvo Rewards with my referral code: ${code}`;
    if (navigator.share) {
        await navigator.share({ title: "Eduvo Rewards referral", text });
    } else {
        await navigator.clipboard.writeText(text);
        event.currentTarget.textContent = "Invite copied";
    }
});
