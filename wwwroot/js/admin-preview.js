document.addEventListener("DOMContentLoaded", function () {
  const fileInputs = document.querySelectorAll("input[type=file][name=ImageFile]");
  fileInputs.forEach(input => {
    const preview = document.createElement("div");
    preview.className = "mt-2";
    input.parentElement?.appendChild(preview);

    input.addEventListener("change", () => {
      const file = input.files?.[0];
      if (!file) {
        preview.innerHTML = "";
        return;
      }
      if (!file.type.startsWith("image/")) {
        preview.innerHTML = "<span class='text-danger small'>Görsel dosyası seçin.</span>";
        return;
      }
      const reader = new FileReader();
      reader.onload = e => {
        preview.innerHTML = `<img src="${e.target?.result}" alt="Preview" style="max-height:120px; border:1px solid #eee; border-radius:8px;">`;
      };
      reader.readAsDataURL(file);
    });
  });

  // Vehicle filter helper
  window.filterVehicles = function (input, listId) {
    const term = input.value.toLowerCase();
    const list = document.getElementById(listId);
    if (!list) return;
    list.querySelectorAll(".form-check").forEach(item => {
      const label = item.innerText.toLowerCase();
      item.style.display = label.includes(term) ? "" : "none";
    });
  };
});
