// Image lightbox - click to zoom
(function () {
    // Create overlay
    const overlay = document.createElement("div");
    overlay.id = "lightbox-overlay";
    const img = document.createElement("img");
    img.id = "lightbox-img";
    overlay.appendChild(img);
    document.body.appendChild(overlay);

    // State
    let scale = 1;
    let translateX = 0;
    let translateY = 0;
    let isDragging = false;
    let dragStartX = 0;
    let dragStartY = 0;
    let lastTranslateX = 0;
    let lastTranslateY = 0;

    function applyTransform() {
        img.style.transform = "translate(" + translateX + "px, " + translateY + "px) scale(" + scale + ")";
    }

    function resetState() {
        scale = 1;
        translateX = 0;
        translateY = 0;
        applyTransform();
    }

    function open(src) {
        img.src = src;
        resetState();
        overlay.classList.add("active");
        document.body.style.overflow = "hidden";
    }

    function close() {
        overlay.classList.remove("active");
        document.body.style.overflow = "";
    }

    // Close on overlay click (not on image)
    overlay.addEventListener("click", function (e) {
        if (e.target === overlay) close();
    });

    // Escape to close
    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") close();
    });

    // Scroll to zoom
    overlay.addEventListener("wheel", function (e) {
        e.preventDefault();
        const delta = e.deltaY > 0 ? 0.9 : 1.1;
        const newScale = Math.min(Math.max(scale * delta, 0.5), 10);

        // Zoom toward cursor
        const rect = img.getBoundingClientRect();
        const cx = rect.left + rect.width / 2;
        const cy = rect.top + rect.height / 2;
        const dx = e.clientX - cx;
        const dy = e.clientY - cy;
        const ratio = 1 - newScale / scale;

        translateX += dx * ratio;
        translateY += dy * ratio;
        scale = newScale;
        applyTransform();
    }, { passive: false });

    // Drag to pan
    img.addEventListener("mousedown", function (e) {
        e.preventDefault();
        isDragging = true;
        dragStartX = e.clientX;
        dragStartY = e.clientY;
        lastTranslateX = translateX;
        lastTranslateY = translateY;
        img.style.cursor = "grabbing";
    });

    document.addEventListener("mousemove", function (e) {
        if (!isDragging) return;
        translateX = lastTranslateX + (e.clientX - dragStartX);
        translateY = lastTranslateY + (e.clientY - dragStartY);
        applyTransform();
    });

    document.addEventListener("mouseup", function () {
        isDragging = false;
        img.style.cursor = "grab";
    });

    // Attach to all content images
    function bindImages() {
        const images = document.querySelectorAll(".content img");
        for (let i = 0; i < images.length; i++) {
            if (images[i].dataset.lightbox) continue;
            images[i].dataset.lightbox = "1";
            images[i].style.cursor = "zoom-in";
            images[i].addEventListener("click", function () {
                open(this.src);
            });
        }
    }

    bindImages();

    // Re-bind on page navigation (mdbook SPA)
    const observer = new MutationObserver(bindImages);
    const content = document.querySelector(".content");
    if (content) observer.observe(content, { childList: true, subtree: true });
})();
