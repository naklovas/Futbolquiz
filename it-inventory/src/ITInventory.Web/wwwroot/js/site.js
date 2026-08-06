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
});
