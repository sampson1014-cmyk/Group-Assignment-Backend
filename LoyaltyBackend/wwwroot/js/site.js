const locationButton = document.querySelector("#locationButton");

const locationStatus = document.querySelector("#locationStatus");
function setLocationStatus(message) {
    if (locationStatus) locationStatus.textContent = message;
}

locationButton?.addEventListener("click", () => {
    if (!navigator.geolocation) {
        locationButton.textContent = "Location is unavailable";
        setLocationStatus("Your browser cannot access your location. You can still select a store and open its map.");
        return;
    }

    locationButton.disabled = true;
    locationButton.textContent = "Finding your location…";
    setLocationStatus("Waiting for your location. Allow location access if your browser asks.");
    navigator.geolocation.getCurrentPosition(position => {
        let updated = 0;
        document.querySelectorAll(".outlet-card").forEach(card => {
            if (!hasValidCoordinates(card)) return;
            const distance = haversine(position.coords.latitude, position.coords.longitude,
                Number(card.dataset.lat), Number(card.dataset.lng));
            card.querySelector(".distance").textContent = `${distance.toFixed(1)} km away`;
            updated++;
        });
        locationButton.disabled = false;
        locationButton.textContent = "Distances updated";
        setLocationStatus(updated ? `Distances updated for ${updated} ${updated === 1 ? "store" : "stores"} with valid map locations. Scroll down to see them.` : "Your location was received, but the listed stores do not have valid map coordinates.");
    }, error => {
        locationButton.disabled = false;
        locationButton.textContent = "Try again";
        setLocationStatus(error.code === 1
            ? "Location access was denied. Allow location for this site in your browser and try again."
            : error.code === 3
                ? "Finding your location took too long. Check that location services are enabled on your device and try again."
                : "Your location could not be found. Check that location services are enabled on your device and try again.");
    }, { timeout: 10000, maximumAge: 60000 });
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

function hasValidCoordinates(card) {
    const lat = Number(card.dataset.lat);
    const lng = Number(card.dataset.lng);
    return Number.isFinite(lat) && Number.isFinite(lng) && Math.abs(lat) <= 90 && Math.abs(lng) <= 180 && !(lat === 0 && lng === 0);
}

function showOutletOnMap(card) {
    const lat = Number(card.dataset.lat);
    const lng = Number(card.dataset.lng);
    if (!outletMap || !hasValidCoordinates(card)) return;
    const span = 0.018;
    const bbox = [lng - span, lat - span, lng + span, lat + span].join(",");
    outletMap.src = `https://www.openstreetmap.org/export/embed.html?bbox=${encodeURIComponent(bbox)}&layer=mapnik&marker=${encodeURIComponent(`${lat},${lng}`)}`;
    if (openMapLink) openMapLink.href = `https://www.openstreetmap.org/?mlat=${encodeURIComponent(lat)}&mlon=${encodeURIComponent(lng)}#map=16/${encodeURIComponent(lat)}/${encodeURIComponent(lng)}`;
    outletCards.forEach(item => item.classList.toggle("selected", item === card));
}

outletCards.forEach(card => {
    const button = card.querySelector(".outlet-map-button");
    if (!hasValidCoordinates(card)) {
        if (button) { button.disabled = true; button.textContent = "Map location unavailable"; }
        return;
    }
    button?.addEventListener("click", () => showOutletOnMap(card));
});
const firstMappableOutlet = outletCards.find(hasValidCoordinates);
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
    const button = event.currentTarget;
    const code = document.querySelector("#referralCode")?.textContent?.trim();
    if (!code) return;
    try {
        await navigator.clipboard.writeText(code);
        button.textContent = "Copied";
        window.EduvoFeedback?.toast("Referral code copied.");
    } catch { button.textContent = "Copy unavailable — select the code"; }
});

document.querySelector("#shareReferral")?.addEventListener("click", async event => {
    const button = event.currentTarget;
    const code = document.querySelector("#referralCode")?.textContent?.trim();
    if (!code) return;
    const text = `Join Eduvo Rewards with my referral code: ${code}`;
    try {
        if (navigator.share) {
            await navigator.share({ title: "Eduvo Rewards referral", text });
        } else {
            await navigator.clipboard.writeText(text);
            button.textContent = "Invite copied";
            window.EduvoFeedback?.toast("Invitation copied.");
        }
    } catch (error) {
        if (error.name !== "AbortError") button.textContent = "Sharing unavailable — copy the code";
    }
});
