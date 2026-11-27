(function () {
    "use strict";

    function getLoc(key, fallback) {
        const host = document.getElementById("esrp-loc-data");
        if (!host) {
            return fallback;
        }
        return host.getAttribute("data-" + key) || fallback;
    }

    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text);
        }
        return new Promise(function (resolve, reject) {
            const area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            try {
                if (document.execCommand("copy")) {
                    resolve();
                } else {
                    reject(new Error("copy command failed"));
                }
            } catch (err) {
                reject(err);
            } finally {
                document.body.removeChild(area);
            }
        });
    }

    document.addEventListener("click", function (event) {
        const copyBtn = event.target.closest("[data-copy-value], [data-copy-target]");
        if (copyBtn) {
            let value = copyBtn.getAttribute("data-copy-value");
            const targetId = copyBtn.getAttribute("data-copy-target");
            if (!value && targetId) {
                const target = document.getElementById(targetId);
                if (target) {
                    value = target.textContent ? target.textContent.trim() : "";
                }
            }
            if (!value) {
                return;
            }
            copyText(value).then(function () {
                const labelEl = copyBtn.querySelector("[data-copy-label]");
                if (labelEl) {
                    if (!copyBtn.dataset.originalLabel) {
                        copyBtn.dataset.originalLabel = labelEl.textContent || "";
                    }
                    labelEl.textContent = getLoc("copied", "Copied");
                }
                copyBtn.classList.add("btn-success");
                copyBtn.classList.remove("btn-outline-secondary", "btn-outline-primary");
                clearTimeout(copyBtn._copyTimer);
                copyBtn._copyTimer = setTimeout(function () {
                    if (labelEl && copyBtn.dataset.originalLabel) {
                        labelEl.textContent = copyBtn.dataset.originalLabel;
                    }
                    copyBtn.classList.remove("btn-success");
                    const restoreClass = copyBtn.getAttribute("data-restore-class") || "btn-outline-secondary";
                    copyBtn.classList.add(restoreClass);
                }, 1800);
            }).catch(function () {
                // Leave original label unchanged if clipboard is unavailable.
            });
            return;
        }

        const toggleBtn = event.target.closest("[data-toggle-password]");
        if (toggleBtn) {
            const inputId = toggleBtn.getAttribute("data-toggle-password");
            const input = inputId ? document.getElementById(inputId) : null;
            if (!input) {
                return;
            }
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            toggleBtn.setAttribute("aria-pressed", show ? "true" : "false");
            toggleBtn.setAttribute("aria-label", show
                ? getLoc("hide-passphrase", "Hide passphrase")
                : getLoc("show-passphrase", "Show passphrase"));
            const showIcon = toggleBtn.querySelector("[data-icon-show]");
            const hideIcon = toggleBtn.querySelector("[data-icon-hide]");
            if (showIcon && hideIcon) {
                showIcon.classList.toggle("d-none", show);
                hideIcon.classList.toggle("d-none", !show);
            }
        }
    });

    document.addEventListener("change", function (event) {
        const input = event.target.closest("[data-file-size-output]");
        if (!input || !input.files || input.files.length === 0) {
            return;
        }
        const outputId = input.getAttribute("data-file-size-output");
        const output = outputId ? document.getElementById(outputId) : null;
        if (!output) {
            return;
        }
        const file = input.files[0];
        const maxBytes = parseInt(input.getAttribute("data-max-bytes") || "67108864", 10);
        const mb = (file.size / (1024 * 1024)).toFixed(2);
        output.textContent = file.name + " (" + mb + " MiB)";
        output.classList.toggle("text-danger", file.size > maxBytes);
        output.classList.toggle("text-muted", file.size <= maxBytes);
    });
})();
