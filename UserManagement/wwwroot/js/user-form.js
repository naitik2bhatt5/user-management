// Add/Edit user form: input masking, field-level validation, error summary, dirty-form guard, double-submit guard.
// Rules mirror the server-side validation in Models/User.cs; the server re-validates everything.
(function () {
    'use strict';

    const form = document.getElementById('userForm');
    if (!form) return;

    const summary = document.getElementById('formSummary');
    const saveButton = document.getElementById('saveButton');
    const cancelLink = document.getElementById('cancelLink');
    const ageHint = document.getElementById('ageHint');
    saveButton.dataset.label = saveButton.textContent;
    const field = name => form.elements.namedItem(name);

    const NAME_RE = /^\p{L}[\p{L} .'\-]*$/u;
    const LABELS = {
        FirstName: 'First name', LastName: 'Last name', DateOfBirth: 'Date of birth', Phone: 'Phone',
        AddressLine1: 'Address line 1', AddressLine2: 'Address line 2', City: 'City', StateCode: 'State',
        Zip5: 'Zip', Zip4: 'Zip+4'
    };

    // ---------- Helpers ----------

    const digitsOnly = value => value.replace(/\D/g, '');
    const clean = value => value.trim().replace(/\s{2,}/g, ' ');

    function todayIso() {
        const d = new Date();
        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    }

    /** Parses yyyy-MM-dd strictly (rejects 2023-02-30 etc). Returns a local Date or null. */
    function parseIsoDate(value) {
        const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
        if (!m) return null;
        const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])];
        const date = new Date(y, mo - 1, d);
        return date.getFullYear() === y && date.getMonth() === mo - 1 && date.getDate() === d ? date : null;
    }

    function ageOn(dob, today = new Date()) {
        let age = today.getFullYear() - dob.getFullYear();
        const hadBirthday = today.getMonth() > dob.getMonth() ||
            (today.getMonth() === dob.getMonth() && today.getDate() >= dob.getDate());
        return hadBirthday ? age : age - 1;
    }

    /** Formats up to 10 digits progressively: "212" -> "(212", "212555" -> "(212) 555", full -> "(212) 555-0143". */
    function formatPhone(digits) {
        if (digits.length === 0) return '';
        if (digits.length <= 3) return `(${digits}`;
        if (digits.length <= 6) return `(${digits.slice(0, 3)}) ${digits.slice(3)}`;
        return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6, 10)}`;
    }

    // ---------- Validation rules: each returns an error message or '' ----------

    function required(name) {
        return v => (v === '' ? `${LABELS[name]} is required.` : '');
    }

    function maxLength(name, max) {
        return v => (v.length > max ? `${LABELS[name]} cannot exceed ${max} characters.` : '');
    }

    function personName(name) {
        return v => (v && !NAME_RE.test(v) ? `${LABELS[name]} may contain letters, spaces, hyphens, apostrophes and periods only.` : '');
    }

    const rules = {
        FirstName: [required('FirstName'), maxLength('FirstName', 50), personName('FirstName')],
        LastName: [required('LastName'), maxLength('LastName', 50), personName('LastName')],
        DateOfBirth: [
            v => {
                // A partially typed date makes input.value '' but validity.badInput true.
                if (field('DateOfBirth').validity.badInput) return 'Enter a complete, valid date.';
                return v === '' ? 'Date of birth is required.' : '';
            },
            v => {
                if (!v) return '';
                const dob = parseIsoDate(v);
                if (!dob) return 'Enter a valid date.';
                if (v > todayIso()) return 'Date of birth cannot be in the future.';
                if (v < '1900-01-01') return 'Date of birth must be on or after 01/01/1900.';
                return '';
            }
        ],
        Phone: [
            required('Phone'),
            v => {
                if (!v) return '';
                let d = digitsOnly(v);
                if (d.length === 11 && d[0] === '1') d = d.slice(1);
                if (d.length !== 10) return 'Phone must be a 10-digit U.S. number.';
                if (!/^[2-9]\d{2}[2-9]\d{6}$/.test(d)) return 'Enter a valid U.S. phone number (area code and exchange cannot start with 0 or 1).';
                return '';
            }
        ],
        AddressLine1: [required('AddressLine1'), maxLength('AddressLine1', 100)],
        AddressLine2: [maxLength('AddressLine2', 100)],
        City: [
            required('City'), maxLength('City', 50),
            v => (v && !/^[\p{L}][\p{L} .'\-]*$/u.test(v) ? 'City may contain letters, spaces, hyphens, apostrophes and periods only.' : '')
        ],
        StateCode: [v => (v === '' ? 'Select a state.' : '')],
        Zip5: [
            required('Zip5'),
            v => (v && !/^\d{5}$/.test(v) ? 'Zip must be exactly 5 digits.' : ''),
            v => (v === '00000' ? '00000 is not a valid zip code.' : '')
        ],
        Zip4: [v => (v && !/^\d{4}$/.test(v) ? 'Zip+4 must be exactly 4 digits.' : '')]
    };

    const touched = new Set();

    function errorElementFor(name) {
        return form.querySelector(`[data-valmsg-for="${name}"]`);
    }

    function showError(name, message) {
        const input = field(name);
        const errorEl = errorElementFor(name);
        input.setAttribute('aria-invalid', message ? 'true' : 'false');
        if (errorEl) {
            errorEl.textContent = message;
            if (!errorEl.id) errorEl.id = `${name}-error`;
            input.setAttribute('aria-describedby', errorEl.id);
        }
    }

    function validateField(name) {
        const input = field(name);
        const value = input.tagName === 'SELECT' ? input.value : clean(input.value);
        const message = rules[name].map(rule => rule(value)).find(Boolean) || '';
        showError(name, message);
        return message;
    }

    function validateAll() {
        const errors = [];
        Object.keys(rules).forEach(name => {
            touched.add(name);
            const message = validateField(name);
            if (message) errors.push({ name, message });
        });
        return errors;
    }

    function renderSummary(errors) {
        if (errors.length === 0) {
            summary.hidden = true;
            summary.innerHTML = '';
            return;
        }
        summary.innerHTML = `<strong>Please fix ${errors.length} field${errors.length === 1 ? '' : 's'}:</strong><ul>` +
            errors.map(e => `<li><a href="#${e.name}" data-field="${e.name}">${App.escapeHtml(e.message)}</a></li>`).join('') +
            '</ul>';
        summary.hidden = false;
    }

    // Clicking an item in the summary focuses that field.
    summary.addEventListener('click', e => {
        const link = e.target.closest('a[data-field]');
        if (!link) return;
        e.preventDefault();
        field(link.dataset.field).focus();
    });

    // ---------- Input masks ----------

    const phone = field('Phone');
    phone.addEventListener('input', () => {
        // Keep the caret after the same number of digits it was after before reformatting.
        const caret = phone.selectionStart ?? phone.value.length;
        const digitsBeforeCaret = digitsOnly(phone.value.slice(0, caret)).length;

        let digits = digitsOnly(phone.value);
        if (digits.length === 11 && digits[0] === '1') digits = digits.slice(1); // pasted "+1 212 555 0143"
        digits = digits.slice(0, 10);
        phone.value = formatPhone(digits);

        let pos = 0, seen = 0;
        while (pos < phone.value.length && seen < digitsBeforeCaret) {
            if (/\d/.test(phone.value[pos])) seen++;
            pos++;
        }
        phone.setSelectionRange(pos, pos);
    });
    // Format a server-provided or autofilled value on load.
    phone.value = formatPhone(digitsOnly(phone.value).slice(-10));

    const zip5 = field('Zip5');
    const zip4 = field('Zip4');

    function handleZipPaste(e) {
        // Pasting "12345-6789" or "123456789" into either box fills both.
        const text = (e.clipboardData || window.clipboardData).getData('text');
        const m = /^\s*(\d{5})(?:[-\s]?(\d{4}))?\s*$/.exec(text);
        if (!m) return;
        e.preventDefault();
        zip5.value = m[1];
        zip4.value = m[2] || '';
        [zip5, zip4].forEach(z => z.dispatchEvent(new Event('input', { bubbles: true })));
        (m[2] ? zip4 : zip5).focus();
    }

    [zip5, zip4].forEach(z => {
        z.addEventListener('paste', handleZipPaste);
        z.addEventListener('input', () => {
            const max = z === zip5 ? 5 : 4;
            const digits = digitsOnly(z.value).slice(0, max);
            if (z.value !== digits) z.value = digits;
        });
    });

    // Auto-advance to Zip+4 once 5 digits are typed at the end of Zip.
    zip5.addEventListener('keyup', e => {
        if (/^\d$/.test(e.key) && zip5.value.length === 5 && zip5.selectionStart === 5 && zip4.value === '') {
            zip4.focus();
        }
    });
    // Backspace in an empty Zip+4 jumps back to Zip.
    zip4.addEventListener('keydown', e => {
        if (e.key === 'Backspace' && zip4.value === '') {
            e.preventDefault();
            zip5.focus();
            zip5.setSelectionRange(zip5.value.length, zip5.value.length);
        }
    });

    // ---------- Date of birth ----------

    const dob = field('DateOfBirth');
    dob.max = todayIso();

    function updateAgeHint() {
        const date = parseIsoDate(dob.value);
        ageHint.textContent = date && dob.value <= todayIso() && dob.value >= '1900-01-01'
            ? `Age: ${ageOn(date)}`
            : '';
    }
    dob.addEventListener('input', updateAgeHint);
    dob.addEventListener('change', updateAgeHint);
    updateAgeHint();

    // ---------- Live validation ----------

    Object.keys(rules).forEach(name => {
        const input = field(name);
        // Validate when the user leaves a field, then on every change once it has been touched.
        input.addEventListener('blur', () => {
            touched.add(name);
            validateField(name);
        });
        input.addEventListener(input.tagName === 'SELECT' ? 'change' : 'input', () => {
            if (touched.has(name)) validateField(name);
            if (!summary.hidden) renderSummary(Object.keys(rules).filter(n => touched.has(n))
                .map(n => ({ name: n, message: errorElementFor(n)?.textContent || '' })).filter(e => e.message));
        });
    });

    // Server-side errors (after a POST round-trip) are already rendered; flag those inputs and show the summary.
    const serverErrors = Object.keys(rules)
        .map(name => ({ name, message: (errorElementFor(name)?.textContent || '').trim() }))
        .filter(e => e.message);
    if (serverErrors.length) {
        serverErrors.forEach(e => { touched.add(e.name); showError(e.name, e.message); });
        renderSummary(serverErrors);
        field(serverErrors[0].name).focus();
    } else {
        field('FirstName').focus();
    }

    // ---------- Unsaved changes guard ----------

    const serialize = () => new URLSearchParams(new FormData(form)).toString();
    let initialState = serialize();
    let submitting = false;
    const isDirty = () => !submitting && serialize() !== initialState;

    window.addEventListener('beforeunload', e => {
        if (isDirty()) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    cancelLink.addEventListener('click', e => {
        if (isDirty() && !confirm('Discard your unsaved changes?')) e.preventDefault();
        else submitting = true; // suppress the beforeunload prompt after confirming
    });

    // ---------- Submit ----------

    let checkingAddress = false;

    function setSaving(saving, label) {
        saveButton.disabled = saving;
        saveButton.textContent = saving ? label : saveButton.dataset.label;
    }

    form.addEventListener('submit', async e => {
        if (submitting || checkingAddress) { // double-click / double Enter
            e.preventDefault();
            return;
        }

        // Trim text inputs so what is validated is what gets saved.
        form.querySelectorAll('input[type="text"], input:not([type])').forEach(i => { i.value = clean(i.value); });

        const errors = validateAll();
        renderSummary(errors);
        if (errors.length) {
            e.preventDefault();
            field(errors[0].name).focus();
            return;
        }

        // Optional Google address check (address-autocomplete.js). Runs only when the address changed
        // since it was last verified, and never blocks saving if Google is unavailable.
        if (window.AddressReview && window.AddressReview.needsReview()) {
            e.preventDefault();
            checkingAddress = true;
            setSaving(true, 'Checking address...');
            let outcome;
            try {
                outcome = await window.AddressReview.review('save');
            } finally {
                checkingAddress = false;
            }
            if (outcome !== 'save') {
                setSaving(false);
                return;
            }
            // A suggested address may have replaced the fields; make sure it still passes.
            const after = validateAll();
            renderSummary(after);
            if (after.length) {
                setSaving(false);
                field(after[0].name).focus();
                return;
            }
            submitting = true;
            setSaving(true, 'Saving...');
            form.submit(); // native submit does not re-run this handler
            return;
        }

        submitting = true;
        setSaving(true, 'Saving...');
    });

    // If the user navigates back to this page from the bfcache, re-enable the form.
    window.addEventListener('pageshow', ev => {
        if (ev.persisted) {
            submitting = false;
            setSaving(false);
            initialState = serialize();
        }
    });

    // Used by address-autocomplete.js to re-check fields it fills in.
    window.UserForm = {
        validateField: name => { touched.add(name); return validateField(name); }
    };
})();
