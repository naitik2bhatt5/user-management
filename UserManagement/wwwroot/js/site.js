// Shared helpers used by the page scripts. Exposed as window.App.
(function () {
    'use strict';

    const messages = document.getElementById('messages');

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    /** Shows a dismissible message at the top of the page. type: 'success' | 'error'. */
    function showMessage(text, type = 'success', autoHideMs = 5000) {
        if (!messages) return;
        const alert = document.createElement('div');
        alert.className = 'alert ' + (type === 'error' ? 'alert-error' : 'alert-success');
        alert.setAttribute('role', type === 'error' ? 'alert' : 'status');
        alert.innerHTML = escapeHtml(text) + '<button type="button" class="alert-close" aria-label="Dismiss">&times;</button>';
        messages.prepend(alert);
        if (autoHideMs && type !== 'error') {
            setTimeout(() => alert.remove(), autoHideMs);
        }
    }

    /** POSTs JSON with the antiforgery token header and returns the parsed JSON body; throws with a readable message on failure. */
    async function postJson(url, body) {
        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        let response;
        try {
            response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json',
                    'RequestVerificationToken': tokenInput ? tokenInput.value : ''
                },
                body: body === undefined ? null : JSON.stringify(body)
            });
        } catch (networkError) {
            throw new Error('Could not reach the server. Check your connection and try again.');
        }

        let data = null;
        const contentType = response.headers.get('Content-Type') || '';
        if (contentType.includes('application/json')) {
            data = await response.json();
        }

        if (!response.ok || (data && data.success === false)) {
            const message = (data && data.message) ||
                (response.status === 400 ? 'The request was rejected. Please refresh the page and try again.'
                                         : `The server returned an error (${response.status}).`);
            throw new Error(message);
        }
        return data;
    }

    function debounce(fn, waitMs) {
        let timer;
        return function (...args) {
            clearTimeout(timer);
            timer = setTimeout(() => fn.apply(this, args), waitMs);
        };
    }

    // Close buttons on alerts (server-rendered and dynamic ones).
    document.addEventListener('click', e => {
        if (e.target.classList.contains('alert-close')) {
            e.target.closest('.alert').remove();
        }
    });

    // Server-rendered success messages fade away on their own.
    document.querySelectorAll('#messages .alert-success').forEach(el => setTimeout(() => el.remove(), 5000));

    window.App = { showMessage, postJson, debounce, escapeHtml };
})();
