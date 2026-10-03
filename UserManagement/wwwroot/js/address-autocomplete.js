// Google-powered address autocomplete (Amazon / Walmart style) and a "Review your address" dialog.
// All Google calls go through /Address/* on our server, so the API key never reaches the browser.
// The page works exactly as before when the feature is off (no API key configured).
(function () {
    'use strict';

    const form = document.getElementById('userForm');
    window.AddressReview = null;
    if (!form || form.dataset.addressApi !== 'true') return;

    const urls = {
        autocomplete: form.dataset.autocompleteUrl,
        place: form.dataset.placeUrl,
        validate: form.dataset.validateUrl
    };
    const field = name => form.elements.namedItem(name);
    const FIELDS = ['AddressLine1', 'AddressLine2', 'City', 'StateCode', 'Zip5', 'Zip4'];
    const line1 = field('AddressLine1');
    const list = document.getElementById('addressSuggestions');
    const statusEl = document.getElementById('addressStatus');
    const verifyButton = document.getElementById('verifyAddress');

    // ---------- Address helpers ----------

    const readAddress = () => Object.fromEntries(FIELDS.map(n => [n, field(n).value.trim()]));
    const fingerprint = (a = readAddress()) => FIELDS.map(n => (a[n] || '').toUpperCase().replace(/[\s.,#]+/g, ' ').trim()).join('|');

    // Server JSON is camelCase (addressLine1); form fields are PascalCase (AddressLine1).
    const fromServer = a => Object.fromEntries(FIELDS.map(n => [n, a[n.charAt(0).toLowerCase() + n.slice(1)] || '']));
    const toServer = a => Object.fromEntries(FIELDS.map(n => [n.charAt(0).toLowerCase() + n.slice(1), a[n] || null]));

    // Fingerprint of the last address that was verified by Google or explicitly confirmed by the user.
    // An existing user's saved address counts as confirmed until it is edited.
    let confirmedFingerprint = form.dataset.addressOnFile === 'true' ? fingerprint() : null;

    function applyAddress(address) {
        FIELDS.forEach(name => {
            const el = field(name);
            const value = address[name] || '';
            if (el.tagName === 'SELECT') {
                if (Array.from(el.options).some(o => o.value === value)) el.value = value;
            } else {
                el.value = value;
            }
            // Let the form's own masks and validation react to the new value.
            el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
            if (window.UserForm) window.UserForm.validateField(name);
            // Brief highlight so the user sees what was filled in.
            el.classList.remove('autofilled');
            void el.offsetWidth;
            el.classList.add('autofilled');
        });
    }

    const addressFieldset = line1.closest('fieldset');
    /** Highlights the whole address section while Google is working on it. */
    function setBusy(on) {
        addressFieldset.classList.toggle('address-busy', on);
        addressFieldset.setAttribute('aria-busy', on ? 'true' : 'false');
    }

    function setStatus(kind, text) {
        statusEl.className = 'address-status ' + (kind ? 'status-' + kind : '');
        statusEl.textContent = text || '';
        statusEl.hidden = !text;
    }

    // Any edit to an address field drops the "verified" badge.
    FIELDS.forEach(name => {
        const el = field(name);
        el.addEventListener(el.tagName === 'SELECT' ? 'change' : 'input', e => {
            if (e.isTrusted && fingerprint() !== confirmedFingerprint && !statusEl.classList.contains('status-loading')) {
                setStatus('', '');
            }
        });
    });

    async function getJson(url, signal) {
        const response = await fetch(url, { headers: { Accept: 'application/json' }, signal });
        if (!response.ok) throw new Error(`Request failed (${response.status})`);
        return response.json();
    }

    // ---------- Autocomplete combobox ----------

    const newSessionToken = () => (window.crypto && crypto.randomUUID)
        ? crypto.randomUUID()
        : Date.now().toString(36) + Math.random().toString(36).slice(2);

    let sessionToken = newSessionToken(); // groups keystrokes + the final pick into one billed Google session
    let suggestions = [];
    let activeIndex = -1;
    let inFlight = null;
    const cache = new Map();

    line1.setAttribute('role', 'combobox');
    line1.setAttribute('aria-autocomplete', 'list');
    line1.setAttribute('aria-controls', list.id);
    line1.setAttribute('aria-expanded', 'false');
    line1.setAttribute('autocomplete', 'off'); // keep the browser's own dropdown from covering ours

    const isOpen = () => !list.hidden;

    function openList() {
        list.hidden = false;
        line1.setAttribute('aria-expanded', 'true');
    }

    function closeList() {
        list.hidden = true;
        activeIndex = -1;
        line1.setAttribute('aria-expanded', 'false');
        line1.removeAttribute('aria-activedescendant');
    }

    /** Escapes text and wraps the parts Google matched in <strong>, like Amazon's suggestion list. */
    function highlight(text, matches) {
        if (!matches || matches.length === 0) return App.escapeHtml(text);
        let html = '', pos = 0;
        matches.slice().sort((a, b) => a.start - b.start).forEach(m => {
            if (m.start < pos) return;
            html += App.escapeHtml(text.slice(pos, m.start)) + '<strong>' + App.escapeHtml(text.slice(m.start, m.end)) + '</strong>';
            pos = m.end;
        });
        return html + App.escapeHtml(text.slice(pos));
    }

    function setActive(index) {
        const items = list.querySelectorAll('.ac-item');
        items.forEach((li, i) => li.setAttribute('aria-selected', i === index ? 'true' : 'false'));
        activeIndex = index;
        if (index >= 0 && items[index]) {
            line1.setAttribute('aria-activedescendant', items[index].id);
            items[index].scrollIntoView({ block: 'nearest' });
        } else {
            line1.removeAttribute('aria-activedescendant');
        }
    }

    function render(message) {
        list.innerHTML = '';
        if (message) {
            list.insertAdjacentHTML('beforeend', `<li class="ac-message" role="presentation">${App.escapeHtml(message)}</li>`);
        }
        suggestions.forEach((s, i) => {
            const li = document.createElement('li');
            li.id = `addressSuggestion-${i}`;
            li.className = 'ac-item';
            li.setAttribute('role', 'option');
            li.setAttribute('aria-selected', 'false');
            li.innerHTML =
                '<span class="ac-pin" aria-hidden="true"></span>' +
                `<span class="ac-text"><span class="ac-main">${highlight(s.mainText, s.mainMatches)}</span>` +
                `<span class="ac-secondary">${App.escapeHtml(s.secondaryText)}</span></span>`;
            // mousedown (not click) so the input's blur doesn't close the list first.
            li.addEventListener('mousedown', e => { e.preventDefault(); choose(i); });
            li.addEventListener('mousemove', () => { if (activeIndex !== i) setActive(i); });
            list.appendChild(li);
        });
        if (suggestions.length || message) {
            list.insertAdjacentHTML('beforeend', '<li class="ac-footer" role="presentation">powered by <b>Google</b></li>');
            openList();
        } else {
            closeList();
        }
    }

    async function fetchSuggestions() {
        const query = line1.value.trim();
        if (query.length < 3) {
            if (inFlight) inFlight.abort();
            setSearching(false);
            suggestions = [];
            closeList();
            return;
        }
        if (cache.has(query)) {
            suggestions = cache.get(query);
            render(suggestions.length ? '' : 'No matching U.S. addresses yet. Keep typing.');
            return;
        }

        if (inFlight) inFlight.abort(); // only the latest keystroke matters
        inFlight = new AbortController();
        const thisRequest = inFlight;
        setSearching(true);
        if (!isOpen() || suggestions.length === 0) {
            suggestions = [];
            render('Searching addresses...');
        }
        try {
            const url = `${urls.autocomplete}?q=${encodeURIComponent(query)}&session=${encodeURIComponent(sessionToken)}`;
            const result = await getJson(url, inFlight.signal);
            cache.set(query, result);
            if (line1.value.trim() !== query || document.activeElement !== line1) return; // stale response
            suggestions = result;
            render(result.length ? '' : 'No matching U.S. addresses yet. Keep typing.');
        } catch (err) {
            if (err.name === 'AbortError') return;
            suggestions = [];
            if (document.activeElement === line1) render('Address suggestions are unavailable. You can keep typing.');
        } finally {
            if (inFlight === thisRequest) setSearching(false);
        }
    }

    const acWrap = line1.closest('.ac-wrap');
    function setSearching(on) {
        acWrap.classList.toggle('searching', on);
        line1.setAttribute('aria-busy', on ? 'true' : 'false');
    }

    const debouncedFetch = App.debounce(fetchSuggestions, 250);

    line1.addEventListener('input', e => {
        if (e.isTrusted === false) return; // value set by applyAddress, not typed
        debouncedFetch();
    });

    line1.addEventListener('keydown', e => {
        const count = suggestions.length;
        switch (e.key) {
            case 'ArrowDown':
                if (!count) return;
                e.preventDefault();
                if (!isOpen()) { render(); setActive(0); } else setActive((activeIndex + 1) % count);
                break;
            case 'ArrowUp':
                if (!count || !isOpen()) return;
                e.preventDefault();
                setActive(activeIndex <= 0 ? count - 1 : activeIndex - 1);
                break;
            case 'Enter':
                if (isOpen() && activeIndex >= 0) {
                    e.preventDefault(); // pick the suggestion instead of submitting the form
                    choose(activeIndex);
                }
                break;
            case 'Escape':
                if (isOpen()) {
                    e.preventDefault();
                    e.stopPropagation();
                    closeList();
                }
                break;
            case 'Tab':
                closeList();
                break;
        }
    });

    line1.addEventListener('blur', () => setTimeout(closeList, 150));
    line1.addEventListener('focus', () => { if (suggestions.length && line1.value.trim().length >= 3) render(); });

    async function choose(index) {
        const s = suggestions[index];
        if (!s) return;
        closeList();
        line1.value = s.mainText;
        setStatus('loading', 'Getting full address from Google...');
        setBusy(true);
        try {
            const url = `${urls.place}/${encodeURIComponent(s.placeId)}?session=${encodeURIComponent(sessionToken)}`;
            const address = fromServer(await getJson(url));
            sessionToken = newSessionToken(); // a pick ends the billing session
            // Keep an apartment number the user already typed if Google's result has none.
            if (!address.AddressLine2) address.AddressLine2 = field('AddressLine2').value.trim();
            if (!address.AddressLine1) address.AddressLine1 = s.mainText;
            applyAddress(address);
            setStatus('selected', 'Address filled in from Google. Add an apartment or suite if needed.');
            // Like Amazon: jump to the apartment/suite field after picking a street address.
            field('AddressLine2').focus();
        } catch {
            setStatus('warning', 'Could not load that address. Please complete the fields below.');
        } finally {
            setBusy(false);
        }
        suggestions = [];
    }

    // ---------- "Review your address" dialog ----------

    const backdrop = document.createElement('div');
    backdrop.className = 'modal-backdrop';
    backdrop.hidden = true;
    backdrop.innerHTML = `
        <div class="modal" role="dialog" aria-modal="true" aria-labelledby="addressDialogTitle" aria-describedby="addressDialogIntro">
            <h2 id="addressDialogTitle"></h2>
            <p id="addressDialogIntro"></p>
            <ul class="modal-issues" hidden></ul>
            <div class="address-options" role="radiogroup" aria-label="Choose an address"></div>
            <div class="modal-actions">
                <button type="button" class="btn btn-primary" data-action="use"></button>
                <button type="button" class="btn" data-action="edit">Edit address</button>
            </div>
        </div>`;
    document.body.appendChild(backdrop);
    const dialog = backdrop.querySelector('.modal');

    function formatAddressHtml(address, compareTo) {
        // Highlight every part that differs from what the user typed.
        const part = name => {
            const value = address[name] || '';
            const changed = compareTo && value.toUpperCase().replace(/[\s.,#]+/g, ' ').trim() !==
                                         (compareTo[name] || '').toUpperCase().replace(/[\s.,#]+/g, ' ').trim();
            return changed && value ? `<mark>${App.escapeHtml(value)}</mark>` : App.escapeHtml(value);
        };
        const zip = part('Zip5') + (address.Zip4 ? '-' + part('Zip4') : '');
        return [
            part('AddressLine1'),
            address.AddressLine2 ? part('AddressLine2') : '',
            `${part('City')}, ${part('StateCode')} ${zip}`
        ].filter(Boolean).join('<br>');
    }

    /** Shows the dialog and resolves with 'suggested' | 'entered' | 'edit'. */
    function showDialog({ result, entered, mode }) {
        setBusy(false);
        const suggested = result.suggested ? fromServer(result.suggested) : null;
        const hasSuggestion = suggested && fingerprint(suggested) !== fingerprint(entered);
        const isSuggested = result.verdict === 'suggested';

        dialog.querySelector('#addressDialogTitle').textContent = isSuggested ? 'Review your address' : "We couldn't verify this address";
        dialog.querySelector('#addressDialogIntro').textContent = isSuggested
            ? 'We found a match with a few changes. Please choose the address to use.'
            : hasSuggestion
                ? 'Google could not fully confirm this address. Check it, pick the closest match, or keep what you entered.'
                : 'Google could not confirm this is a known address. Check it, or keep what you entered.';

        const issues = dialog.querySelector('.modal-issues');
        issues.innerHTML = (result.issues || []).map(i => `<li>${App.escapeHtml(i)}</li>`).join('');
        issues.hidden = !(result.issues && result.issues.length);

        const options = dialog.querySelector('.address-options');
        options.innerHTML = '';
        const addOption = (value, label, html, checked) => {
            options.insertAdjacentHTML('beforeend', `
                <label class="address-option">
                    <input type="radio" name="addressChoice" value="${value}" ${checked ? 'checked' : ''} />
                    <span><span class="option-label">${label}</span><span class="option-address">${html}</span></span>
                </label>`);
        };
        if (hasSuggestion) addOption('suggested', isSuggested ? 'Suggested address' : 'Closest match', formatAddressHtml(suggested, entered), true);
        addOption('entered', 'Address as entered', formatAddressHtml(entered), !hasSuggestion);

        const useButton = dialog.querySelector('[data-action="use"]');
        const updateUseLabel = () => {
            const choice = options.querySelector('input:checked').value;
            useButton.textContent = mode === 'save'
                ? (choice === 'suggested' ? 'Use suggested address & save' : 'Save as entered')
                : (choice === 'suggested' ? 'Use suggested address' : 'Keep as entered');
        };
        options.onchange = updateUseLabel;
        updateUseLabel();

        const previousFocus = document.activeElement;
        backdrop.hidden = false;
        document.body.classList.add('modal-open');
        (options.querySelector('input:checked') || useButton).focus();

        return new Promise(resolve => {
            const finish = outcome => {
                backdrop.hidden = true;
                document.body.classList.remove('modal-open');
                dialog.removeEventListener('keydown', onKeydown);
                backdrop.removeEventListener('mousedown', onBackdrop);
                useButton.onclick = editButton.onclick = null;
                if (outcome !== 'edit' && previousFocus) previousFocus.focus();
                resolve(outcome);
            };
            const onKeydown = e => {
                if (e.key === 'Escape') { e.preventDefault(); finish('edit'); return; }
                if (e.key !== 'Tab') return;
                // Keep keyboard focus inside the dialog.
                const focusable = Array.from(dialog.querySelectorAll('input:checked, button')).filter(el => !el.disabled);
                const first = focusable[0], last = focusable[focusable.length - 1];
                if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
                else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
            };
            const onBackdrop = e => { if (e.target === backdrop) finish('edit'); };
            const editButton = dialog.querySelector('[data-action="edit"]');
            useButton.onclick = () => finish(options.querySelector('input:checked').value);
            editButton.onclick = () => finish('edit');
            dialog.addEventListener('keydown', onKeydown);
            backdrop.addEventListener('mousedown', onBackdrop);
        });
    }

    let reviewing = false;

    /**
     * Validates the current address with Google.
     * mode 'save'  -> resolves 'save' when the form may be submitted, 'edit' when the user wants to fix it.
     * mode 'check' -> same flow from the "Verify address" button; the caller does not submit.
     */
    async function review(mode) {
        if (reviewing) return 'edit';
        reviewing = true;
        verifyButton.disabled = true;
        const entered = readAddress();
        setStatus('loading', 'Verifying address with Google...');
        setBusy(true);

        try {
            let result;
            try {
                result = await App.postJson(urls.validate, toServer(entered));
            } catch {
                result = { verdict: 'unavailable' };
            }

            if (result.verdict === 'unavailable') {
                setStatus('warning', 'Address could not be verified right now.');
                return mode === 'save' ? 'save' : 'edit'; // never block saving because Google is unreachable
            }

            if (result.verdict === 'verified') {
                // Google may still standardize casing or add Zip+4; apply silently.
                if (result.suggested) applyAddress(fromServer(result.suggested));
                confirmedFingerprint = fingerprint();
                setStatus('verified', 'Verified address');
                return 'save';
            }

            const choice = await showDialog({ result, entered, mode });
            if (choice === 'edit') {
                setStatus('warning', result.verdict === 'suggested' ? 'Please review the address.' : 'Address not verified.');
                const focusName = result.needsUnitNumber ? 'AddressLine2' : 'AddressLine1';
                field(focusName).focus();
                field(focusName).select?.();
                return 'edit';
            }

            if (choice === 'suggested') {
                applyAddress(fromServer(result.suggested));
                confirmedFingerprint = fingerprint();
                setStatus('verified', 'Verified address');
            } else {
                confirmedFingerprint = fingerprint();
                setStatus('confirmed', 'Kept as entered (not verified by Google)');
            }
            return 'save';
        } finally {
            setBusy(false);
            reviewing = false;
            verifyButton.disabled = false;
        }
    }

    verifyButton.hidden = false;
    verifyButton.addEventListener('click', async () => {
        // Make sure the address fields are filled in before asking Google.
        const missing = ['AddressLine1', 'City', 'StateCode', 'Zip5']
            .filter(n => window.UserForm && window.UserForm.validateField(n));
        if (missing.length) {
            field(missing[0]).focus();
            return;
        }
        await review('check');
    });

    window.AddressReview = {
        /** True when the address changed since it was last verified or confirmed. */
        needsReview: () => !reviewing && fingerprint() !== confirmedFingerprint,
        review
    };
})();
