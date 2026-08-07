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
// without data-col (e.g. the actions column) are always shown. Choice is
// remembered per table via localStorage.
function initColumnChooser(table) {
    var storageKey = table.getAttribute('data-colchooser');
    var btn = document.getElementById('col-chooser-btn-' + table.id);
    var panel = document.getElementById('col-chooser-panel-' + table.id);
    var headerCells = table.querySelectorAll('thead th[data-col]');
    if (!storageKey || !btn || !panel || headerCells.length === 0) return;

    var hidden = [];
    try { hidden = JSON.parse(localStorage.getItem(storageKey) || '[]'); } catch (e) { hidden = []; }

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
