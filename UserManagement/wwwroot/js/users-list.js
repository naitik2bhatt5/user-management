// Users list: search, sorting, single delete, checkbox selection (incl. shift-click ranges) and bulk delete.
(function () {
    'use strict';

    const table = document.getElementById('usersTable');
    if (!table) return;

    const tbody = table.tBodies[0];
    const selectAll = document.getElementById('selectAll');
    const deleteSelectedBtn = document.getElementById('deleteSelected');
    const selectedCountEl = document.getElementById('selectedCount');
    const userCountEl = document.getElementById('userCount');
    const searchBox = document.getElementById('userSearch');
    const emptyRow = document.getElementById('emptyRow');
    const noMatchRow = document.getElementById('noMatchRow');
    const deleteUrl = table.dataset.deleteUrl;
    const deleteMultipleUrl = table.dataset.deleteMultipleUrl;

    let lastClickedRow = null; // anchor for shift-click range selection
    let busy = false;          // blocks overlapping delete requests

    const allRows = () => Array.from(tbody.querySelectorAll('tr[data-id]'));
    const visibleRows = () => allRows().filter(r => !r.hidden);
    const checkboxOf = row => row.querySelector('.row-select');
    const selectedRows = () => allRows().filter(r => checkboxOf(r).checked);

    // ---------- Selection ----------

    function setRowChecked(row, checked) {
        checkboxOf(row).checked = checked;
        row.classList.toggle('selected', checked);
    }

    function refreshUi() {
        const visible = visibleRows();
        const total = allRows().length;
        const checkedVisible = visible.filter(r => checkboxOf(r).checked).length;
        const selected = selectedRows().length;

        // Header checkbox: checked when every visible row is selected, indeterminate when only some are.
        selectAll.checked = visible.length > 0 && checkedVisible === visible.length;
        selectAll.indeterminate = checkedVisible > 0 && checkedVisible < visible.length;
        selectAll.disabled = visible.length === 0 || busy;

        selectedCountEl.textContent = selected;
        deleteSelectedBtn.disabled = selected === 0 || busy;

        userCountEl.textContent = visible.length === total
            ? `${total} user${total === 1 ? '' : 's'}`
            : `Showing ${visible.length} of ${total} users`;

        emptyRow.hidden = total !== 0;
        noMatchRow.hidden = !(total > 0 && visible.length === 0);
    }

    selectAll.addEventListener('change', () => {
        // Only affects rows the user can currently see (respects the search filter).
        visibleRows().forEach(r => setRowChecked(r, selectAll.checked));
        lastClickedRow = null;
        refreshUi();
    });

    tbody.addEventListener('click', e => {
        const checkbox = e.target.closest('.row-select');
        if (!checkbox) return;
        const row = checkbox.closest('tr');

        // Shift+click selects/deselects every visible row between the last clicked row and this one.
        if (e.shiftKey && lastClickedRow && lastClickedRow !== row && !lastClickedRow.hidden && lastClickedRow.isConnected) {
            const rows = visibleRows();
            const [from, to] = [rows.indexOf(lastClickedRow), rows.indexOf(row)].sort((a, b) => a - b);
            rows.slice(from, to + 1).forEach(r => setRowChecked(r, checkbox.checked));
        } else {
            setRowChecked(row, checkbox.checked);
        }
        lastClickedRow = row;
        refreshUi();
    });

    // ---------- Search ----------

    function applyFilter() {
        const terms = searchBox.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
        allRows().forEach(row => {
            const haystack = row.dataset.search || '';
            // Strip non-digits from numeric-looking terms so "212555" matches "(212) 555-0143".
            const match = terms.every(t => haystack.includes(t) || (/\d/.test(t) && haystack.includes(t.replace(/\D/g, ''))));
            row.hidden = !match;
            // Never keep hidden rows selected, so a bulk delete only removes what is on screen.
            if (!match && checkboxOf(row).checked) setRowChecked(row, false);
        });
        refreshUi();
    }

    searchBox.addEventListener('input', App.debounce(applyFilter, 150));
    searchBox.addEventListener('keydown', e => {
        if (e.key === 'Escape') {
            searchBox.value = '';
            applyFilter();
        }
    });

    // ---------- Sorting ----------

    const headers = Array.from(table.tHead.rows[0].cells);

    function sortBy(th) {
        const columnIndex = headers.indexOf(th);
        const type = th.dataset.sortType;
        const direction = th.getAttribute('aria-sort') === 'ascending' ? 'descending' : 'ascending';
        const factor = direction === 'ascending' ? 1 : -1;

        headers.forEach(h => h.removeAttribute('aria-sort'));
        th.setAttribute('aria-sort', direction);

        const valueOf = row => {
            const cell = row.cells[columnIndex];
            return (cell.dataset.sort ?? cell.textContent).trim();
        };
        const collator = new Intl.Collator(undefined, { sensitivity: 'base', numeric: true });

        const rows = allRows();
        rows.sort((a, b) => {
            const va = valueOf(a), vb = valueOf(b);
            // Dates are rendered as yyyy-MM-dd in data-sort, so string order equals chronological order.
            const result = type === 'number' ? (parseFloat(va) || 0) - (parseFloat(vb) || 0) : collator.compare(va, vb);
            return result * factor || (Number(a.dataset.id) - Number(b.dataset.id)); // stable tie-break
        });
        rows.forEach(r => tbody.appendChild(r));
    }

    headers.filter(th => th.classList.contains('sortable')).forEach(th => {
        th.addEventListener('click', () => sortBy(th));
        th.addEventListener('keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                sortBy(th);
            }
        });
    });

    // ---------- Deleting ----------

    function removeRows(rows) {
        rows.forEach(row => {
            if (row === lastClickedRow) lastClickedRow = null;
            row.classList.add('removing');
            setTimeout(() => { row.remove(); refreshUi(); }, 300);
        });
    }

    function setBusy(value) {
        busy = value;
        tbody.querySelectorAll('.delete-user').forEach(b => { b.disabled = value; });
        refreshUi();
    }

    tbody.addEventListener('click', async e => {
        const button = e.target.closest('.delete-user');
        if (!button || busy) return;
        const row = button.closest('tr');
        const name = row.dataset.name;

        if (!confirm(`Delete user "${name}"? This cannot be undone.`)) return;

        setBusy(true);
        try {
            const result = await App.postJson(`${deleteUrl}/${encodeURIComponent(row.dataset.id)}`);
            removeRows([row]);
            App.showMessage(result.deleted
                ? `User "${name}" was deleted.`
                : `User "${name}" had already been deleted.`);
        } catch (err) {
            App.showMessage(err.message, 'error');
        } finally {
            setBusy(false);
        }
    });

    deleteSelectedBtn.addEventListener('click', async () => {
        const rows = selectedRows();
        if (rows.length === 0 || busy) return;

        const preview = rows.slice(0, 5).map(r => ' - ' + r.dataset.name).join('\n') +
            (rows.length > 5 ? `\n ...and ${rows.length - 5} more` : '');
        if (!confirm(`Delete ${rows.length} selected user${rows.length === 1 ? '' : 's'}?\n\n${preview}\n\nThis cannot be undone.`)) return;

        setBusy(true);
        try {
            const ids = rows.map(r => Number(r.dataset.id));
            const result = await App.postJson(deleteMultipleUrl, { ids });
            removeRows(rows);
            const missing = result.requested - result.deleted;
            App.showMessage(`${result.deleted} user${result.deleted === 1 ? ' was' : 's were'} deleted.` +
                (missing > 0 ? ` ${missing} had already been removed.` : ''));
        } catch (err) {
            App.showMessage(err.message, 'error');
        } finally {
            setBusy(false);
        }
    });

    // Keep header checkbox, counter and button correct if the browser restores checkbox state on back/forward.
    window.addEventListener('pageshow', () => {
        allRows().forEach(r => r.classList.toggle('selected', checkboxOf(r).checked));
        if (searchBox.value) applyFilter(); else refreshUi();
    });

    refreshUi();
})();
