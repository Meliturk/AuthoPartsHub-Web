// Otomatik miktar güncelleme: [data-autosubmit] içeren input değişince form gönder
document.addEventListener("change", function (e) {
    const target = e.target;
    if (target instanceof HTMLInputElement && target.dataset.autosubmit !== undefined) {
        const form = target.closest("form");
        if (form) form.submit();
    }
});

// Navbar sepet sayacı doldurma (server-render için ViewBag kullanılabilir; burada basit placeholder)
// Not: Eğer ViewBag.Count kullanılacaksa _Layout'ta server tarafı eklenmeli.

// Hero dropdown seçimleri
document.addEventListener("click", function (e) {
    if (e.target instanceof HTMLElement && e.target.matches(".dropdown-menu a[data-target]")) {
        e.preventDefault();
        const target = e.target.getAttribute("data-target");
        const value = e.target.getAttribute("data-value") || "";
        const text = e.target.textContent || "(tümü)";
        const input = document.getElementById(`${target}Input`);
        const label = document.getElementById(`${target}Text`);
        if (input) input.value = value;
        if (label) label.textContent = text;
        const dropdown = e.target.closest(".dropdown");
        const btn = dropdown?.querySelector("[data-bs-toggle='dropdown']");
        if (btn) btn.dispatchEvent(new Event("click")); // collapse
    }
});
