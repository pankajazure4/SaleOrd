// Global helpers used across the app.
(function () {
    function ensureToastHost() {
        var host = document.getElementById('toastHost');
        if (host) return host;

        host = document.createElement('div');
        host.id = 'toastHost';
        host.className = 'toast-container position-fixed top-0 end-0 p-3';
        host.style.zIndex = '1080';
        document.body.appendChild(host);
        return host;
    }

    function toastClass(type) {
        switch ((type || '').toLowerCase()) {
            case 'success':
                return { icon: 'check-circle-fill', title: 'Success', accent: 'toast-success' };
            case 'warning':
                return { icon: 'exclamation-triangle-fill', title: 'Warning', accent: 'toast-warning' };
            case 'danger':
            case 'error':
                return { icon: 'x-circle-fill', title: 'Error', accent: 'toast-danger' };
            default:
                return { icon: 'info-circle-fill', title: 'Info', accent: 'toast-info' };
        }
    }

    window.showToast = function (type, message, title) {
        if (!window.bootstrap || !bootstrap.Toast) return;

        var meta = toastClass(type);
        var host = ensureToastHost();
        var el = document.createElement('div');
        el.className = 'toast app-toast align-items-center border-0 ' + meta.accent;
        el.setAttribute('role', 'alert');
        el.setAttribute('aria-live', 'assertive');
        el.setAttribute('aria-atomic', 'true');
        el.innerHTML = [
            '<div class="d-flex">',
            '<div class="toast-body d-flex align-items-start gap-2">',
            '<i class="bi bi-' + meta.icon + ' mt-1 flex-shrink-0"></i>',
            '<div><div class="fw-semibold small">' + (title || meta.title) + '</div><div class="small">' + (message || '') + '</div></div>',
            '</div>',
            '<button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>',
            '</div>'
        ].join('');

        host.appendChild(el);
        var toast = new bootstrap.Toast(el, { delay: 3200 });
        el.addEventListener('hidden.bs.toast', function () { el.remove(); });
        toast.show();
    };

    window.showSuccess = function (message, title) { window.showToast('success', message, title); };
    window.showError = function (message, title) { window.showToast('danger', message, title); };
})();
