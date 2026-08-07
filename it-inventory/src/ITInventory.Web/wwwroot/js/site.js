// Shared vanilla JS used across the app: delete-confirmation modal + client-side table search.

function openConfirmModal(formId, message) {
    var modal = document.getElementById('confirm-modal');
    var msgEl = document.getElementById('confirm-modal-message');
    var confirmBtn = document.getElementById('confirm-modal-confirm');
    if (!modal || !msgEl || !confirmBtn) return;

    msgEl.textContent = message;
    confirmBtn.onclick = function () {
        var form = document.getElementById(formId);
        if (form) form.submit();
    };
    modal.classList.remove('hidden');
}

function closeConfirmModal() {
    var modal = document.getElementById('confirm-modal');
    if (modal) modal.classList.add('hidden');
}

function showLoadingOverlay() {
    var overlay = document.getElementById('loading-overlay');
    if (overlay) overlay.classList.remove('hidden');
}

function hideLoadingOverlay() {
    var overlay = document.getElementById('loading-overlay');
    if (overlay) overlay.classList.add('hidden');
}

// Turns a Branch <select> into a combobox of branch names scoped to the
// currently selected Country. `locations` is an array of { CountryId, Branch }
// for every active Location. Re-filters whenever the #CountryId select/hidden
// input changes (and works unchanged for non-admins, whose country never
// changes). `currentValue` is the value the field was bound to on page load
// (Model.Branch) -- if it isn't in the filtered Locations list (legacy
// free-text data, or a value for a different country), it's kept as an extra
// selected option instead of being silently dropped.
function initBranchCombobox(selectId, locations, currentValue) {
    var select = document.getElementById(selectId);
    if (!select) return;
    var usedInitialValue = false;

    function refresh() {
        var countryField = document.getElementById('CountryId');
        var countryId = countryField ? parseInt(countryField.value, 10) : NaN;
        var value = usedInitialValue ? select.value : (currentValue || '');
        usedInitialValue = true;

        select.innerHTML = '';
        var placeholder = document.createElement('option');
        placeholder.value = '';
        placeholder.textContent = '-- Select --';
        select.appendChild(placeholder);

        var seen = {};
        var matched = false;
        (locations || []).forEach(function (loc) {
            if (!isNaN(countryId) && loc.CountryId !== countryId) return;
            if (seen[loc.Branch]) return;
            seen[loc.Branch] = true;
            var opt = document.createElement('option');
            opt.value = loc.Branch;
            opt.textContent = loc.Branch;
            if (loc.Branch === value) { opt.selected = true; matched = true; }
            select.appendChild(opt);
        });

        if (value && !matched) {
            var extra = document.createElement('option');
            extra.value = value;
            extra.textContent = value;
            extra.selected = true;
            select.insertBefore(extra, select.children[1] || null);
        }
    }

    var countryField = document.getElementById('CountryId');
    if (countryField) countryField.addEventListener('change', refresh);
    refresh();
}

// Populates a <datalist> of suggestions for a free-text input from a flat
// list of distinct values already used elsewhere in the same table (ex:
// Vendor/Supplier, License Info) -- no separate lookup table involved, and
// the field stays plain free text, just with suggestions as you type.
function initTextAutocomplete(inputId, values) {
    var input = document.getElementById(inputId);
    if (!input) return;

    var listId = inputId + '-suggest-datalist';
    var datalist = document.getElementById(listId);
    if (!datalist) {
        datalist = document.createElement('datalist');
        datalist.id = listId;
        input.insertAdjacentElement('afterend', datalist);
        input.setAttribute('list', listId);
    }

    datalist.innerHTML = '';
    var seen = {};
    (values || []).forEach(function (v) {
        if (!v || seen[v]) return;
        seen[v] = true;
        var opt = document.createElement('option');
        opt.value = v;
        datalist.appendChild(opt);
    });
}

function filterRows(inputEl, tableId) {
    var filter = inputEl.value.toLowerCase();
    var rows = document.querySelectorAll('#' + tableId + ' tbody tr');
    rows.forEach(function (row) {
        var text = row.textContent.toLowerCase();
        row.style.display = text.indexOf(filter) !== -1 ? '' : 'none';
    });
}

// Column chooser: any <table id="..." data-colchooser="storage-key"> gets a
// "Columns" show/hide dropdown, wired to a button/panel with matching IDs
// (col-chooser-btn-<tableId> / col-chooser-panel-<tableId>). Column headers
// need data-col="<key>" on both the <th> and its matching <td>s; cells
// without data-col (e.g. the actions column) are always shown. A header can
// add data-default-hidden="true" for extra/detail columns that should start
// hidden the very first time a user opens this table (before they've saved
// any preference of their own). Choice is remembered per table via
// localStorage.
function initColumnChooser(table) {
    var storageKey = table.getAttribute('data-colchooser');
    var btn = document.getElementById('col-chooser-btn-' + table.id);
    var panel = document.getElementById('col-chooser-panel-' + table.id);
    var headerCells = table.querySelectorAll('thead th[data-col]');
    if (!storageKey || !btn || !panel || headerCells.length === 0) return;

    var saved = localStorage.getItem(storageKey);
    var hidden = [];
    if (saved === null) {
        headerCells.forEach(function (th) {
            if (th.getAttribute('data-default-hidden') === 'true') hidden.push(th.getAttribute('data-col'));
        });
    } else {
        try { hidden = JSON.parse(saved); } catch (e) { hidden = []; }
    }

    function applyVisibility() {
        headerCells.forEach(function (th) {
            var col = th.getAttribute('data-col');
            var isHidden = hidden.indexOf(col) !== -1;
            table.querySelectorAll('[data-col="' + col + '"]').forEach(function (cell) {
                cell.classList.toggle('hidden', isHidden);
            });
        });
    }

    panel.innerHTML = '';
    headerCells.forEach(function (th) {
        var col = th.getAttribute('data-col');
        var label = document.createElement('label');
        label.className = 'flex items-center gap-2 px-3 py-1.5 text-sm text-slate-700 hover:bg-slate-50 cursor-pointer';

        var checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = hidden.indexOf(col) === -1;
        checkbox.className = 'rounded border-slate-300 text-brand-600 focus:ring-brand-500';
        checkbox.addEventListener('change', function () {
            if (checkbox.checked) {
                hidden = hidden.filter(function (c) { return c !== col; });
            } else if (hidden.indexOf(col) === -1) {
                hidden.push(col);
            }
            localStorage.setItem(storageKey, JSON.stringify(hidden));
            applyVisibility();
        });

        label.appendChild(checkbox);
        label.appendChild(document.createTextNode(th.textContent.trim()));
        panel.appendChild(label);
    });

    applyVisibility();

    btn.addEventListener('click', function (e) {
        e.stopPropagation();
        panel.classList.toggle('hidden');
    });
    document.addEventListener('click', function (e) {
        if (!panel.classList.contains('hidden') && !panel.contains(e.target) && e.target !== btn) {
            panel.classList.add('hidden');
        }
    });
}

document.addEventListener('DOMContentLoaded', function () {
    var toggle = document.getElementById('mobile-menu-toggle');
    var menu = document.getElementById('mobile-menu');
    if (toggle && menu) {
        toggle.addEventListener('click', function () {
            menu.classList.toggle('hidden');
        });
    }

    var modal = document.getElementById('confirm-modal');
    if (modal) {
        modal.addEventListener('click', function (e) {
            if (e.target === modal) closeConfirmModal();
        });
    }

    document.querySelectorAll('table[data-colchooser]').forEach(initColumnChooser);
});

// If the browser restores this page from back/forward cache (bfcache) with the
// loading overlay still showing from just before navigating away, hide it.
window.addEventListener('pageshow', function (e) {
    if (e.persisted) hideLoadingOverlay();
});
