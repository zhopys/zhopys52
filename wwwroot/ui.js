(function () {
    'use strict';

    var BYN_SYMBOL = '\uE901';
    var BYN_FONT = 'NBRB';
    var bynFontPromise = null;

    function ensureBynFont() {
        if (!document.fonts || bynFontPromise) return bynFontPromise;
        bynFontPromise = new FontFace(BYN_FONT, 'url(/fonts/nbrb/nbrb.woff2)')
            .load()
            .then(function (font) {
                document.fonts.add(font);
            })
            .catch(function () { /* ignore */ });
        return bynFontPromise;
    }

    function withBynSuffix(value) {
        return value + ' ' + BYN_SYMBOL;
    }

    var balanceHeaderPattern = /прибыл|остаток|баланс|нетто|profit|balance|net\s*flow|маржа/i;

    function parseAmount(text) {
        if (!text) return null;
        var cleaned = text.replace(/\s/g, '').replace(/Br/gi, '').replace(/\uE901/g, '').replace(/[^\d,.\-+]/g, '');
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

    function wrapTablesForMobile(root) {
        var scope = root || document;
        scope.querySelectorAll('table.table').forEach(function (table) {
            if (table.closest('.table-responsive')) return;
            var parent = table.parentElement;
            if (!parent) return;
            var wrap = document.createElement('div');
            wrap.className = 'table-responsive table-shell';
            parent.insertBefore(wrap, table);
            wrap.appendChild(table);
        });
    }

    function enhance(root) {
        applyNegativeBalanceRows(root);
        animateCards(root);
        wrapTablesForMobile(root);
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
        animateCards: function () { animateCards(document); },
        scrollToId: function (elementId) {
            var el = document.getElementById(elementId);
            if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    };

    var profitChartInstance = null;
    var donutChartInstance = null;
    var forecastChartInstance = null;
    var forecastChartsByCanvas = {};

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

    function movingAverage(values, windowSize) {
        if (!values || values.length === 0) return [];
        var w = Math.max(1, windowSize || 1);
        var out = [];
        for (var i = 0; i < values.length; i++) {
            var from = Math.max(0, i - w + 1);
            var sum = 0;
            var count = 0;
            for (var j = from; j <= i; j++) {
                sum += values[j];
                count++;
            }
            out.push(sum / count);
        }
        return out;
    }

    function downsampleForecastSeries(labels, balances, gapRanges, maxPoints) {
        if (!labels || labels.length <= maxPoints) {
            return { labels: labels.slice(), balances: balances.slice(), gapRanges: gapRanges || [] };
        }

        var last = labels.length - 1;
        var step = last / (maxPoints - 1);
        var newLabels = [];
        var newBalances = [];
        var picked = [];

        for (var k = 0; k < maxPoints; k++) {
            var idx = Math.round(k * step);
            if (idx > last) idx = last;
            if (picked.indexOf(idx) >= 0) continue;
            picked.push(idx);
            newLabels.push(labels[idx]);
            newBalances.push(balances[idx]);
        }

        if (picked.indexOf(0) < 0) {
            newLabels.unshift(labels[0]);
            newBalances.unshift(balances[0]);
            picked.unshift(0);
        }
        if (picked.indexOf(last) < 0) {
            newLabels.push(labels[last]);
            newBalances.push(balances[last]);
        }

        var remapped = (gapRanges || []).map(function (range) {
            if (!range || range.length < 2) return range;
            return [
                findNearestPickedIndex(picked, range[0]),
                findNearestPickedIndex(picked, range[1])
            ];
        });

        return { labels: newLabels, balances: newBalances, gapRanges: remapped };
    }

    function findNearestPickedIndex(picked, targetIdx) {
        var best = 0;
        var bestDist = Infinity;
        for (var i = 0; i < picked.length; i++) {
            var dist = Math.abs(picked[i] - targetIdx);
            if (dist < bestDist) {
                bestDist = dist;
                best = i;
            }
        }
        return best;
    }

    function prepareForecastSeries(labels, balances, gapRanges) {
        var smoothWindow = labels.length > 60 ? 7 : labels.length > 30 ? 5 : 3;
        var smoothed = movingAverage(balances, smoothWindow);
        var sampled = downsampleForecastSeries(labels, smoothed, gapRanges, 40);
        return sampled;
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
                                        ? withBynSuffix(v.toLocaleString('ru-RU', { maximumFractionDigits: 0 }))
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
        renderForecastChart: function (canvasId, dates, balances, gapRanges, minThreshold) {
            var canvas = document.getElementById(canvasId);
            if (!canvas || typeof Chart === 'undefined') return;
            if (!dates || !balances || dates.length === 0 || balances.length === 0) return;

            if (forecastChartsByCanvas[canvasId]) {
                forecastChartsByCanvas[canvasId].destroy();
                delete forecastChartsByCanvas[canvasId];
            }

            var c = chartColors();
            var threshold = typeof minThreshold === 'number' ? minThreshold : 0;
            gapRanges = gapRanges || [];

            function parseIsoDate(iso) {
                return new Date(iso + 'T12:00:00').getTime();
            }

            function formatBr(v) {
                if (typeof v !== 'number' || isNaN(v)) return v;
                var abs = Math.abs(v);
                if (abs >= 1000000) return (v / 1000000).toFixed(1) + 'M';
                if (abs >= 10000) return (v / 1000).toFixed(0) + 'k';
                return v.toLocaleString('ru-RU', { maximumFractionDigits: 0 });
            }

            function formatByn(v) {
                return withBynSuffix(formatBr(v));
            }

            function formatDateTick(ts) {
                return new Date(ts).toLocaleDateString('ru-RU', { day: '2-digit', month: 'short' });
            }

            function niceNum(range, round) {
                var exponent = Math.floor(Math.log10(range));
                var fraction = range / Math.pow(10, exponent);
                var niceFraction = round
                    ? (fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10)
                    : (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
                return niceFraction * Math.pow(10, exponent);
            }

            function niceAxis(min, max, ticks) {
                if (min === max) {
                    min -= 1;
                    max += 1;
                }
                var range = niceNum(max - min, false);
                var step = niceNum(range / Math.max(ticks - 1, 1), true);
                var niceMin = Math.floor(min / step) * step;
                var niceMax = Math.ceil(max / step) * step;
                return { min: niceMin, max: niceMax, step: step };
            }

            ensureBynFont();

            var series = dates.map(function (d, i) {
                return { x: parseIsoDate(d), y: Number(balances[i]) || 0, iso: d };
            });

            var yValues = series.map(function (p) { return p.y; });
            if (threshold > 0) yValues.push(threshold);
            yValues.push(0);

            var dataMin = Math.min.apply(null, yValues);
            var dataMax = Math.max.apply(null, yValues);
            var span = dataMax - dataMin;
            var yPad = span > 0 ? span * 0.1 : Math.max(Math.abs(dataMax), 1) * 0.1;
            var yAxis = niceAxis(dataMin - yPad, dataMax + yPad, 6);

            var todayTs = parseIsoDate(dates[0]);
            var xMin = series[0].x;
            var xMax = series[series.length - 1].x;
            var dayMs = 24 * 60 * 60 * 1000;
            var tickEveryDays = Math.max(1, Math.ceil((xMax - xMin) / dayMs / 7));

            var chart = new Chart(canvas.getContext('2d'), {
                type: 'line',
                data: {
                    datasets: [{
                        label: 'Остаток на счёте',
                        data: series,
                        parsing: false,
                        borderColor: c.teal,
                        backgroundColor: 'rgba(0, 212, 170, 0.12)',
                        fill: 'origin',
                        tension: 0.3,
                        cubicInterpolationMode: 'monotone',
                        borderWidth: 2,
                        borderJoinStyle: 'round',
                        pointRadius: series.length <= 35 ? 0 : 0,
                        pointHoverRadius: 5,
                        pointHitRadius: 12,
                        pointBackgroundColor: c.teal
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    interaction: { mode: 'nearest', axis: 'x', intersect: false },
                    layout: { padding: { top: 16, right: 8, bottom: 4, left: 4 } },
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            backgroundColor: c.tooltipBg,
                            borderColor: c.tooltipBorder,
                            borderWidth: 1,
                            titleColor: c.text,
                            bodyColor: c.text,
                            callbacks: {
                                title: function (items) {
                                    if (!items || !items.length) return '';
                                    var p = items[0].raw;
                                    return new Date(p.x).toLocaleDateString('ru-RU', {
                                        day: '2-digit',
                                        month: 'long',
                                        year: 'numeric'
                                    });
                                },
                                label: function (ctx) {
                                    var v = ctx.parsed.y;
                                    var s = formatByn(v);
                                    if (v < 0) s += ' · разрыв';
                                    else if (threshold > 0 && v < threshold) s += ' · ниже порога';
                                    return s;
                                }
                            }
                        }
                    },
                    scales: {
                        x: {
                            type: 'linear',
                            min: xMin,
                            max: xMax,
                            bounds: 'ticks',
                            grid: { color: 'rgba(148,163,184,0.08)', drawTicks: true },
                            border: { display: false },
                            ticks: {
                                color: c.text,
                                maxRotation: 0,
                                autoSkip: false,
                                maxTicksLimit: 8,
                                callback: function (val) {
                                    var daysFromStart = Math.round((val - xMin) / dayMs);
                                    if (daysFromStart % tickEveryDays !== 0 && Math.abs(val - todayTs) > dayMs * 0.5) {
                                        return '';
                                    }
                                    return formatDateTick(val);
                                }
                            }
                        },
                        y: {
                            min: yAxis.min,
                            max: yAxis.max,
                            bounds: 'ticks',
                            grid: { color: c.grid, drawBorder: false },
                            border: { display: false },
                            ticks: {
                                color: c.text,
                                stepSize: yAxis.step,
                                font: { family: "'" + BYN_FONT + "', Inter, sans-serif", size: 11 },
                                callback: function (v) { return formatByn(v); }
                            }
                        }
                    }
                },
                plugins: [{
                    id: 'cashForecastOverlays',
                    beforeDatasetsDraw: function (ch) {
                        var ctx = ch.ctx;
                        var xScale = ch.scales.x;
                        var area = ch.chartArea;
                        if (!xScale || !area) return;

                        gapRanges.forEach(function (range) {
                            if (!range || range.length < 2) return;
                            var i0 = Math.max(0, Math.min(range[0], series.length - 1));
                            var i1 = Math.max(i0, Math.min(range[1], series.length - 1));
                            var px0 = xScale.getPixelForValue(series[i0].x);
                            var px1 = xScale.getPixelForValue(series[i1].x);
                            ctx.save();
                            ctx.fillStyle = 'rgba(239, 68, 68, 0.12)';
                            ctx.fillRect(Math.min(px0, px1), area.top, Math.abs(px1 - px0), area.bottom - area.top);
                            ctx.restore();
                        });
                    },
                    afterDatasetsDraw: function (ch) {
                        var ctx = ch.ctx;
                        var xScale = ch.scales.x;
                        var yScale = ch.scales.y;
                        var area = ch.chartArea;
                        if (!xScale || !yScale || !area) return;

                        var drawHLine = function (value, color, dash, label) {
                            var py = yScale.getPixelForValue(value);
                            if (py < area.top || py > area.bottom) return;
                            ctx.save();
                            ctx.strokeStyle = color;
                            ctx.lineWidth = 1;
                            ctx.setLineDash(dash || []);
                            ctx.beginPath();
                            ctx.moveTo(area.left, py);
                            ctx.lineTo(area.right, py);
                            ctx.stroke();
                            if (label) {
                                ctx.fillStyle = color;
                                ctx.font = '10px "' + BYN_FONT + '", system-ui, sans-serif';
                                ctx.textAlign = 'right';
                                ctx.fillText(label, area.right - 4, py - 4);
                            }
                            ctx.restore();
                        };

                        drawHLine(0, 'rgba(239, 68, 68, 0.5)', [5, 4], formatByn(0));
                        if (threshold > 0) {
                            drawHLine(threshold, 'rgba(245, 158, 11, 0.7)', [4, 4], 'Порог ' + formatBr(threshold) + ' ' + BYN_SYMBOL);
                        }

                        var tx = xScale.getPixelForValue(todayTs);
                        if (tx >= area.left && tx <= area.right) {
                            ctx.save();
                            ctx.strokeStyle = 'rgba(59, 130, 246, 0.6)';
                            ctx.lineWidth = 1.5;
                            ctx.setLineDash([4, 4]);
                            ctx.beginPath();
                            ctx.moveTo(tx, area.top);
                            ctx.lineTo(tx, area.bottom);
                            ctx.stroke();
                            ctx.fillStyle = 'rgba(59, 130, 246, 0.9)';
                            ctx.font = '10px system-ui, sans-serif';
                            ctx.textAlign = 'center';
                            ctx.fillText('Сегодня', tx, area.top + 12);
                            ctx.restore();
                        }
                    }
                }]
            });

            forecastChartsByCanvas[canvasId] = chart;
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
            Object.keys(forecastChartsByCanvas).forEach(function (id) {
                if (forecastChartsByCanvas[id]) forecastChartsByCanvas[id].destroy();
            });
            forecastChartsByCanvas = {};
            if (window._weekdayChartInstance) { window._weekdayChartInstance.destroy(); window._weekdayChartInstance = null; }
        }
    };
})();
