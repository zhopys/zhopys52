(function () {
    'use strict';

    var balanceHeaderPattern = /прибыл|остаток|баланс|нетто|profit|balance|net\s*flow|маржа/i;

    function parseAmount(text) {
        if (!text) return null;
        var cleaned = text.replace(/\s/g, '').replace(/Br/gi, '').replace(/[^\d,.\-+]/g, '');
        if (!cleaned || cleaned === '-' || cleaned === '+') return null;
        var normalized = cleaned.indexOf(',') > -1 && cleaned.indexOf('.') === -1
            ? cleaned.replace(',', '.')
            : cleaned.replace(/,/g, '');
        var value = parseFloat(normalized);
        return isNaN(value) ? null : value;
    }

    function findBalanceColumnIndex(table) {
        var headers = table.querySelectorAll('thead th');
        for (var i = 0; i < headers.length; i++) {
            if (balanceHeaderPattern.test(headers[i].textContent || '')) {
                return i;
            }
        }
        return -1;
    }

    function applyNegativeBalanceRows(root) {
        var scope = root || document;
        var tables = scope.querySelectorAll('table.table');

        tables.forEach(function (table) {
            var balanceCol = findBalanceColumnIndex(table);
            var rows = table.querySelectorAll('tbody tr');

            rows.forEach(function (row) {
                var value = null;

                if (row.hasAttribute('data-balance')) {
                    value = parseFloat(row.getAttribute('data-balance'));
                } else if (balanceCol >= 0) {
                    var cells = row.querySelectorAll('td');
                    if (cells[balanceCol]) {
                        value = parseAmount(cells[balanceCol].textContent);
                    }
                } else {
                    var amountCell = row.querySelector('td.amount, td[data-amount]');
                    if (amountCell) {
                        value = amountCell.hasAttribute('data-amount')
                            ? parseFloat(amountCell.getAttribute('data-amount'))
                            : parseAmount(amountCell.textContent);
                    }
                }

                if (value !== null && !isNaN(value) && value < 0) {
                    row.classList.add('negative-balance');
                } else {
                    row.classList.remove('negative-balance');
                }
            });
        });
    }

    function animateCards(root) {
        var scope = root || document;
        var cards = scope.querySelectorAll('.project-card[data-animate]:not(.show), .stat-card[data-animate]:not(.show)');

        cards.forEach(function (card, index) {
            setTimeout(function () {
                card.classList.add('show');
            }, Math.min(index * 45, 400));
        });
    }

    function enhance(root) {
        applyNegativeBalanceRows(root);
        animateCards(root);
    }

    function init() {
        enhance(document);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    if (typeof Blazor !== 'undefined') {
        Blazor.addEventListener('enhancedload', function () {
            requestAnimationFrame(init);
        });
    }

    window.uiEnhance = {
        refresh: init,
        applyNegativeBalanceRows: function () { applyNegativeBalanceRows(document); },
        animateCards: function () { animateCards(document); }
    };

    var profitChartInstance = null;
    var donutChartInstance = null;
    var forecastChartInstance = null;

    function chartColors() {
        var dark = document.documentElement.getAttribute('data-theme') === 'dark';
        return {
            teal: getComputedStyle(document.documentElement).getPropertyValue('--chart-teal').trim() || '#00d4aa',
            blue: getComputedStyle(document.documentElement).getPropertyValue('--chart-blue').trim() || '#3b82f6',
            grid: dark ? 'rgba(148, 163, 184, 0.08)' : 'rgba(15, 23, 42, 0.06)',
            text: dark ? '#94a3b8' : '#64748b',
            tooltipBg: dark ? 'rgba(12, 18, 32, 0.95)' : 'rgba(255, 255, 255, 0.98)',
            tooltipBorder: dark ? 'rgba(0, 212, 170, 0.2)' : 'rgba(15, 23, 42, 0.1)'
        };
    }

    window.dashboardCharts = {
        renderProfitChart: function (canvasId, labels, profitData, incomeData, expenseData) {
            var canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;

            if (profitChartInstance) {
                profitChartInstance.destroy();
                profitChartInstance = null;
            }

            var c = chartColors();
            var ctx = canvas.getContext('2d');
            var gradient = ctx.createLinearGradient(0, 0, 0, 280);
            gradient.addColorStop(0, c.teal.replace(')', ', 0.35)').replace('rgb', 'rgba').replace('#00d4aa', 'rgba(0, 212, 170'));
            if (c.teal.indexOf('#') === 0) {
                gradient.addColorStop(0, 'rgba(0, 212, 170, 0.35)');
                gradient.addColorStop(1, 'rgba(0, 212, 170, 0)');
            } else {
                gradient.addColorStop(0, c.teal + '59');
                gradient.addColorStop(1, 'transparent');
            }

            profitChartInstance = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [
                        {
                            label: 'Чистая прибыль',
                            data: profitData,
                            borderColor: c.teal,
                            backgroundColor: 'rgba(0, 212, 170, 0.12)',
                            fill: true,
                            tension: 0.42,
                            borderWidth: 2.5,
                            pointRadius: 0,
                            pointHoverRadius: 5,
                            pointHoverBackgroundColor: c.teal,
                            pointHoverBorderColor: '#fff',
                            pointHoverBorderWidth: 2
                        },
                        {
                            label: 'Доход',
                            data: incomeData,
                            borderColor: c.blue,
                            backgroundColor: 'transparent',
                            fill: false,
                            tension: 0.42,
                            borderWidth: 2,
                            borderDash: [0],
                            pointRadius: 0,
                            pointHoverRadius: 4
                        },
                        {
                            label: 'Расход',
                            data: expenseData,
                            borderColor: 'rgba(251, 113, 133, 0.85)',
                            backgroundColor: 'transparent',
                            fill: false,
                            tension: 0.42,
                            borderWidth: 1.5,
                            borderDash: [6, 4],
                            pointRadius: 0,
                            pointHoverRadius: 4
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: c.tooltipBg,
                            titleColor: '#f1f5f9',
                            bodyColor: '#94a3b8',
                            borderColor: c.tooltipBorder,
                            borderWidth: 1,
                            padding: 12,
                            cornerRadius: 10,
                            displayColors: true,
                            callbacks: {
                                label: function (ctx) {
                                    var v = ctx.parsed.y;
                                    var formatted = typeof v === 'number'
                                        ? v.toLocaleString('ru-RU', { maximumFractionDigits: 0 }) + ' Br'
                                        : v;
                                    return ctx.dataset.label + ': ' + formatted;
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            grid: { display: false },
                            ticks: { color: c.text, font: { family: 'Inter', size: 11 } }
                        },
                        y: {
                            grid: { color: c.grid },
                            ticks: {
                                color: c.text,
                                font: { family: 'Inter', size: 11 },
                                callback: function (v) {
                                    return v >= 1000 || v <= -1000
                                        ? (v / 1000).toFixed(0) + 'k'
                                        : v;
                                }
                            }
                        }
                    }
                }
            });
        },
        renderDonutChart: function (canvasId, labels, values, colors, dotNetRef) {
            var canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;
            if (donutChartInstance) { donutChartInstance.destroy(); donutChartInstance = null; }
            var c = chartColors();
            donutChartInstance = new Chart(canvas.getContext('2d'), {
                type: 'doughnut',
                data: {
                    labels: labels,
                    datasets: [{
                        data: values,
                        backgroundColor: colors && colors.length ? colors : ['#ef4444', '#f59e0b', '#3b82f6', '#8b5cf6', '#10b981'],
                        borderWidth: 0,
                        hoverOffset: 6
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    cutout: '62%',
                    onClick: function (evt, elements) {
                        if (elements && elements[0] && dotNetRef) {
                            dotNetRef.invokeMethodAsync('OnSliceClick', elements[0].index);
                        }
                    },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: c.tooltipBg,
                            callbacks: {
                                label: function (ctx) {
                                    return ctx.label + ': ' + ctx.parsed + '%';
                                }
                            }
                        }
                    }
                }
            });
        },
        renderForecastChart: function (canvasId, labels, balances, gapStarts, gapEnds) {
            var canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;
            if (forecastChartInstance) { forecastChartInstance.destroy(); forecastChartInstance = null; }
            var c = chartColors();
            var pointColors = balances.map(function (v) { return v < 0 ? 'rgba(239, 68, 68, 0.9)' : c.teal; });
            var segmentColors = balances.map(function (v, i) {
                if (v >= 0) return 'rgba(0, 212, 170, 0.15)';
                return 'rgba(239, 68, 68, 0.2)';
            });
            forecastChartInstance = new Chart(canvas.getContext('2d'), {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Прогноз баланса',
                        data: balances,
                        borderColor: c.teal,
                        backgroundColor: function (context) {
                            var v = context.parsed && context.parsed.y;
                            return v < 0 ? 'rgba(239, 68, 68, 0.25)' : 'rgba(0, 212, 170, 0.12)';
                        },
                        fill: true,
                        tension: 0.35,
                        borderWidth: 2,
                        pointRadius: 0,
                        pointHoverRadius: 4,
                        segment: {
                            borderColor: function (ctx) {
                                return ctx.p1.parsed.y < 0 || ctx.p0.parsed.y < 0 ? 'rgba(239, 68, 68, 0.85)' : c.teal;
                            }
                        }
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'index', intersect: false },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (ctx) {
                                    var v = ctx.parsed.y;
                                    var s = typeof v === 'number' ? v.toLocaleString('ru-RU', { maximumFractionDigits: 0 }) + ' Br' : v;
                                    if (v < 0) s += ' ⚠ разрыв';
                                    return s;
                                }
                            }
                        }
                    },
                    scales: {
                        x: { grid: { display: false }, ticks: { color: c.text, maxTicksLimit: 8 } },
                        y: {
                            grid: { color: c.grid },
                            ticks: {
                                color: c.text,
                                callback: function (v) { return (v / 1000).toFixed(0) + 'k'; }
                            }
                        }
                    }
                }
            });
        },
        renderWeekdayChart: function (canvasId, labels, values) {
            var canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;
            if (window._weekdayChartInstance) { window._weekdayChartInstance.destroy(); window._weekdayChartInstance = null; }
            var c = chartColors();
            window._weekdayChartInstance = new Chart(canvas.getContext('2d'), {
                type: 'bar',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Расходы',
                        data: values,
                        backgroundColor: 'rgba(0, 212, 170, 0.65)',
                        borderColor: c.teal,
                        borderWidth: 1,
                        borderRadius: 4
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { grid: { display: false }, ticks: { color: c.text } },
                        y: {
                            grid: { color: c.grid },
                            ticks: {
                                color: c.text,
                                callback: function (v) { return (v / 1000).toFixed(0) + 'k'; }
                            }
                        }
                    }
                }
            });
        },
        destroy: function () {
            if (profitChartInstance) { profitChartInstance.destroy(); profitChartInstance = null; }
            if (donutChartInstance) { donutChartInstance.destroy(); donutChartInstance = null; }
            if (forecastChartInstance) { forecastChartInstance.destroy(); forecastChartInstance = null; }
            if (window._weekdayChartInstance) { window._weekdayChartInstance.destroy(); window._weekdayChartInstance = null; }
        }
    };
})();
