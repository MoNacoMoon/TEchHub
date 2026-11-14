// ==========================================================================
// ЭЛЕКТРОННЫЙ УЧЕБНЫЙ КАБИНЕТ - КЛИЕНТСКИЕ СКРИПТЫ (site.js)
// ==========================================================================

const STORAGE_KEY = 'educabinet_theme';

// 1. ФУНКЦИЯ ПЕРЕКЛЮЧЕНИЯ ТЕМЫ ОФОРМЛЕНИЯ (CINEMATIC DARK & CRISP EDITORIAL)
window.applyAppTheme = function (theme) {
    if (theme === 'dark') {
        document.documentElement.setAttribute('data-theme', 'dark');
        document.documentElement.setAttribute('data-bs-theme', 'dark');
        document.documentElement.classList.add('dark');
    } else {
        document.documentElement.setAttribute('data-theme', 'light');
        document.documentElement.setAttribute('data-bs-theme', 'light');
        document.documentElement.classList.remove('dark');
    }

    try {
        localStorage.setItem(STORAGE_KEY, theme);
    } catch (e) {}

    const fav = document.getElementById('appFavicon');
    if (fav) {
        fav.href = (theme === 'light') ? '/favicon-light.png' : '/favicon.png';
    }

    window.updateThemeUI(theme);
};

window.updateThemeUI = function (theme) {
    const label = document.getElementById('themeLabel');
    const mobileLabel = document.getElementById('mobileThemeLabel');
    const btn = document.getElementById('themeToggleBtn');
    if (label) {
        label.textContent = '';
    }
    if (mobileLabel) {
        mobileLabel.textContent = (theme === 'dark') ? 'Включить светлую тему' : 'Включить тёмную тему';
    }
    if (btn) {
        const nextTitle = (theme === 'dark') ? 'Переключить на светлую тему' : 'Переключить на тёмную тему';
        btn.setAttribute('title', nextTitle);
        btn.setAttribute('aria-label', nextTitle);
    }
};

let isTogglingTheme = false;
window.toggleAppTheme = function (e) {
    if (e && e.preventDefault) e.preventDefault();
    if (isTogglingTheme) return;
    isTogglingTheme = true;
    setTimeout(() => { isTogglingTheme = false; }, 400);

    const current = document.documentElement.getAttribute('data-bs-theme') || 
                    document.documentElement.getAttribute('data-theme') || 'light';
    const next = current === 'dark' ? 'light' : 'dark';

    const performThemeSwitch = () => {
        window.applyAppTheme(next);
    };

    // Современный View Transitions API для идеально плавного GPU-кроссфейда без рывков
    if (document.startViewTransition && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        document.startViewTransition(() => {
            performThemeSwitch();
        });
    } else {
        performThemeSwitch();
    }

    // Тактильный акустический микро-отклик (Web Audio API)
    try {
        const AudioCtx = window.AudioContext || window.webkitAudioContext;
        if (AudioCtx) {
            const ctx = new AudioCtx();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(next === 'dark' ? 520 : 700, ctx.currentTime);
            gain.gain.setValueAtTime(0.015, ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.0001, ctx.currentTime + 0.05);
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start();
            osc.stop(ctx.currentTime + 0.05);
        }
    } catch(err) {}
};

// Инициализация темы при загрузке скрипта
(function () {
    let saved = 'light';
    try {
        saved = localStorage.getItem(STORAGE_KEY) || 
            (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    } catch (e) {}

    window.applyAppTheme(saved);

    function onReady() {
        window.updateThemeUI(saved);

        const btn = document.getElementById('themeToggleBtn');
        if (btn && !btn.hasAttribute('onclick') && !btn.hasAttribute('data-bound')) {
            btn.setAttribute('data-bound', 'true');
            btn.addEventListener('click', function (e) {
                e.preventDefault();
                window.toggleAppTheme(e);
            });
        }

        // Интерактивный амбиентный трекинг курсора (Mouse Spotlight Glow)
        document.addEventListener('pointermove', function (e) {
            const cards = document.querySelectorAll('.craft-card, .course-card, .active-task-card, .stat-tile, .card, .landing-hero, .portal-role-card');
            cards.forEach(card => {
                const rect = card.getBoundingClientRect();
                const x = e.clientX - rect.left;
                const y = e.clientY - rect.top;
                if (x >= -40 && x <= rect.width + 40 && y >= -40 && y <= rect.height + 40) {
                    card.style.setProperty('--mouse-x', `${x}px`);
                    card.style.setProperty('--mouse-y', `${y}px`);
                }
            });
        }, { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', onReady);
    } else {
        onReady();
    }
})();

// 2. РЕЖИМ ФОКУСНОГО ЧТЕНИЯ ЛЕКЦИИ (ZEN / FOCUS READING MODE)
window.toggleZenMode = function () {
    const isZen = document.body.classList.toggle('zen-mode-active');
    const btn = document.getElementById('zenModeBtn');
    if (btn) {
        if (isZen) {
            btn.innerHTML = '<i class="bi bi-fullscreen-exit me-1"></i>Обычный режим';
            btn.classList.remove('btn-outline-secondary');
            btn.classList.add('btn-warning');
        } else {
            btn.innerHTML = '<i class="bi bi-arrows-fullscreen me-1"></i>Режим чтения';
            btn.classList.remove('btn-warning');
            btn.classList.add('btn-outline-secondary');
        }
    }
};

document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && document.body.classList.contains('zen-mode-active')) {
        window.toggleZenMode();
    }
});

// 3. СИСТЕМА ВСПЛЫВАЮЩИХ УВЕДОМЛЕНИЙ (TOAST NOTIFICATIONS)
window.EduCabinet = window.EduCabinet || {};
window.EduCabinet.showToast = function (message, type = 'success', icon = 'bi-check-circle-fill') {
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        container.className = 'toast-container position-fixed bottom-0 end-0 p-3';
        container.style.zIndex = '2050';
        document.body.appendChild(container);
    }

    const toastEl = document.createElement('div');
    toastEl.className = `toast align-items-center text-bg-${type} border-0 show shadow-lg mb-2`;
    toastEl.setAttribute('role', 'alert');
    toastEl.setAttribute('aria-live', 'assertive');
    toastEl.setAttribute('aria-atomic', 'true');
    toastEl.innerHTML = `
        <div class="d-flex">
            <div class="toast-body d-flex align-items-center gap-2">
                <i class="bi ${icon} fs-5"></i>
                <div class="fw-semibold">${message}</div>
            </div>
            <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
        </div>
    `;
    container.appendChild(toastEl);

    setTimeout(() => {
        toastEl.classList.remove('show');
        setTimeout(() => toastEl.remove(), 400);
    }, 3500);
};

// 4. КНОПКА «КОПИРОВАТЬ КОД» В БЛОКАХ ТЕОРИИ
function initCodeCopyButtons() {
    document.querySelectorAll('pre').forEach(pre => {
        if (pre.querySelector('.copy-code-btn')) return;

        const copyBtn = document.createElement('button');
        copyBtn.type = 'button';
        copyBtn.className = 'btn btn-sm btn-dark copy-code-btn shadow-sm';
        copyBtn.innerHTML = '<i class="bi bi-clipboard me-1"></i>Копировать';
        copyBtn.title = 'Скопировать блок кода в буфер обмена';

        copyBtn.addEventListener('click', function (e) {
            e.stopPropagation();
            const codeText = pre.querySelector('code') ? pre.querySelector('code').innerText : pre.innerText;
            navigator.clipboard.writeText(codeText).then(() => {
                copyBtn.innerHTML = '<i class="bi bi-check2 text-success me-1"></i>Скопировано!';
                copyBtn.classList.remove('btn-dark');
                copyBtn.classList.add('btn-success');
                if (window.EduCabinet && window.EduCabinet.showToast) {
                    window.EduCabinet.showToast('Фрагмент кода скопирован в буфер обмена', 'success', 'bi-clipboard-check-fill');
                }
                setTimeout(() => {
                    copyBtn.innerHTML = '<i class="bi bi-clipboard me-1"></i>Копировать';
                    copyBtn.classList.remove('btn-success');
                    copyBtn.classList.add('btn-dark');
                }, 2000);
            });
        });

        pre.appendChild(copyBtn);
    });
}

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initCodeCopyButtons);
} else {
    initCodeCopyButtons();
}

// 5. ПОЛОСА ПРОГРЕССА ЧТЕНИЯ ЛЕКЦИИ (READING PROGRESS BAR)
window.initReadingProgressBar = function () {
    const bar = document.getElementById('readingProgressBar');
    if (!bar) return;

    window.addEventListener('scroll', function () {
        const winScroll = document.documentElement.scrollTop || document.body.scrollTop;
        const height = document.documentElement.scrollHeight - document.documentElement.clientHeight;
        const scrolled = height > 0 ? (winScroll / height) * 100 : 0;
        bar.style.width = Math.min(100, Math.max(0, scrolled)) + '%';
    });
};

// 6. ЛИЧНЫЕ ЗАМЕТКИ СТУДЕНТА К ЛЕКЦИИ
window.initLectureNotes = function (lectureId, studentId) {
    const noteArea = document.getElementById('lectureNotesArea');
    const statusEl = document.getElementById('lectureNotesStatus');
    if (!noteArea) return;

    const key = `educabinet_note_lec_${lectureId}_st_${studentId}`;
    try {
        const saved = localStorage.getItem(key);
        if (saved) {
            noteArea.value = saved;
        }
    } catch(e) {}

    let timer;
    noteArea.addEventListener('input', function () {
        if (statusEl) statusEl.innerHTML = '<i class="bi bi-pencil me-1"></i>Сохранение...';
        clearTimeout(timer);
        timer = setTimeout(() => {
            try {
                localStorage.setItem(key, noteArea.value);
            } catch(e) {}
            if (statusEl) statusEl.innerHTML = '<i class="bi bi-check2 text-success me-1"></i>Сохранено в браузере';
        }, 500);
    });
};

// 7. КОМАНДНАЯ ПАНЕЛЬ БЫСТРОГО ПОИСКА (COMMAND PALETTE: CTRL + K)
const commandPaletteItems = [
    { title: 'Мои учебные дисциплины', url: '/Student', role: 'student', icon: 'bi-journal-bookmark-fill', category: 'Навигация' },
    { title: 'Панель преподавателя (Обзор)', url: '/Teacher', role: 'teacher', icon: 'bi-speedometer2', category: 'Навигация' },
    { title: 'Управление дисциплинами и модулями', url: '/Teacher/Disciplines', role: 'teacher', icon: 'bi-folder2-open', category: 'Управление' },
    { title: 'Проверка развернутых ответов и эссе', url: '/Teacher/ManualReviews', role: 'teacher', icon: 'bi-check2-circle', category: 'Проверка' },
    { title: 'Сводный журнал успеваемости и Excel экспорт', url: '/Teacher/Reports', role: 'teacher', icon: 'bi-bar-chart-line', category: 'Аналитика' },
    { title: 'Учебные группы и Радар студентов', url: '/Teacher/Students', role: 'teacher', icon: 'bi-people', category: 'Студенты' },
    { title: 'Журнал безопасности и аудит системы', url: '/Teacher/AuditLog', role: 'teacher', icon: 'bi-shield-check', category: 'Безопасность' },
    { title: 'Резервное копирование и бэкапы БД', url: '/Teacher/Backup', role: 'teacher', icon: 'bi-database-down', category: 'Система' },
    { title: 'Переключить тему оформления (Светлая / Тёмная)', action: 'toggleTheme', icon: 'bi-moon-stars', category: 'Интерфейс' },
    { title: 'Главная страница портала', url: '/', icon: 'bi-house-door', category: 'Общее' }
];

let commandPaletteModal = null;

window.openCommandPalette = function () {
    const el = document.getElementById('commandPaletteModal');
    if (!el) return;
    if (!commandPaletteModal) {
        commandPaletteModal = new bootstrap.Modal(el);
    }
    commandPaletteModal.show();
    setTimeout(() => {
        const input = document.getElementById('commandPaletteSearch');
        if (input) {
            input.value = '';
            input.focus();
            renderCommandPaletteResults('');
        }
    }, 200);
};

window.closeCommandPalette = function () {
    if (commandPaletteModal) {
        commandPaletteModal.hide();
    }
};

function renderCommandPaletteResults(query) {
    const container = document.getElementById('commandPaletteResults');
    if (!container) return;

    const q = (query || '').toLowerCase().trim();
    const filtered = commandPaletteItems.filter(item => {
        if (!q) return true;
        return item.title.toLowerCase().includes(q) || item.category.toLowerCase().includes(q);
    });

    if (filtered.length === 0) {
        container.innerHTML = `
            <div class="p-4 text-center text-muted">
                <i class="bi bi-search fs-2 mb-2 d-block opacity-50"></i>
                <div class="fw-semibold">Ничего не найдено</div>
                <div class="small">Попробуйте ввести другое ключевое слово</div>
            </div>
        `;
        return;
    }

    let html = '';
    filtered.forEach((item, idx) => {
        const activeClass = idx === 0 ? 'active' : '';
        const actionAttr = item.action ? `onclick="executeCommand('${item.action}')"` : `href="${item.url}"`;
        html += `
            <a ${actionAttr} class="list-group-item list-group-item-action command-palette-item d-flex align-items-center justify-content-between ${activeClass}">
                <div class="d-flex align-items-center gap-3">
                    <div class="command-icon-box text-primary">
                        <i class="bi ${item.icon} fs-5"></i>
                    </div>
                    <div>
                        <div class="fw-semibold small">${item.title}</div>
                        <span class="badge bg-light text-muted border text-uppercase" style="font-size: 0.65rem;">${item.category}</span>
                    </div>
                </div>
                <i class="bi bi-arrow-return-left text-muted small"></i>
            </a>
        `;
    });

    container.innerHTML = html;
}

window.executeCommand = function (action) {
    window.closeCommandPalette();
    if (action === 'toggleTheme') {
        window.toggleAppTheme();
    }
};

// Горячая клавиша Ctrl + K / Cmd + K
document.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K' || e.key === 'л' || e.key === 'Л')) {
        e.preventDefault();
        window.openCommandPalette();
    }
});

// Живой поиск в Command Palette
document.addEventListener('DOMContentLoaded', function () {
    const input = document.getElementById('commandPaletteSearch');
    if (input) {
        input.addEventListener('input', function () {
            renderCommandPaletteResults(this.value);
        });
    }
});

// 8. ЖИВАЯ ФИЛЬТРАЦИЯ И ПОИСК КУРСОВ (DISCIPLINE LIVE FILTER & SEARCH)
window.filterDisciplines = function (category, btn) {
    const group = document.getElementById('disciplineFilterGroup');
    if (group && btn) {
        group.querySelectorAll('.btn').forEach(b => {
            b.classList.remove('active', 'btn-primary');
            b.classList.add('btn-outline-secondary');
        });
        btn.classList.add('active', 'btn-primary');
        btn.classList.remove('btn-outline-secondary');
    }

    const cards = document.querySelectorAll('.discipline-item-card');
    cards.forEach(card => {
        const isCompleted = card.getAttribute('data-completed') === 'true';
        if (category === 'all') {
            card.style.display = '';
        } else if (category === 'completed') {
            card.style.display = isCompleted ? '' : 'none';
        } else if (category === 'in-progress') {
            card.style.display = !isCompleted ? '' : 'none';
        }
    });
};

window.searchDisciplines = function (query) {
    const q = (query || '').toLowerCase().trim();
    const cards = document.querySelectorAll('.discipline-item-card');
    cards.forEach(card => {
        const title = (card.getAttribute('data-title') || '').toLowerCase();
        const desc = (card.getAttribute('data-desc') || '').toLowerCase();
        if (!q || title.includes(q) || desc.includes(q)) {
            card.style.display = '';
        } else {
            card.style.display = 'none';
        }
    });
};
