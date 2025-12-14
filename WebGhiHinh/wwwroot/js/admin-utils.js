// FILE: wwwroot/js/admin-utils.js
window.adminUtils = {
    copyText: async (text) => {
        try {
            if (!text) return false;

            // Modern clipboard
            if (navigator.clipboard && window.isSecureContext) {
                await navigator.clipboard.writeText(text);
                return true;
            }

            // Fallback
            const ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "fixed";
            ta.style.opacity = "0";
            document.body.appendChild(ta);
            ta.focus();
            ta.select();
            const ok = document.execCommand("copy");
            document.body.removeChild(ta);
            return !!ok;
        } catch {
            return false;
        }
    }
};
