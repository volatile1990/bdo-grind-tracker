(function () {
    "use strict";

    const WIDTH = 1200;
    const MAX_HEIGHT = 16384;
    const PAD = 48;
    const INNER = WIDTH - PAD * 2;
    const GAP = 18;
    const FONT = '"Segoe UI", system-ui, sans-serif';
    const COLORS = {
        background: "#101719", panel: "#182225", line: "#2b3a3d",
        text: "#eff3f1", muted: "#a3b3b5", teal: "#79d2bd", gold: "#e3bc79"
    };
    const string = value => value == null ? "" : String(value);
    const list = value => Array.isArray(value) ? value : [];

    // Snapshot before the first await: an active session can change while its artwork loads.
    function snapshot(data) {
        if (!data || typeof data !== "object") throw new Error("Missing session image data.");
        const fields = ["fileName", "title", "subtitle", "backgroundUrl", "iconUrl",
            "lootHeading", "consumablesHeading", "quantityLabel", "hourlyLabel", "footer"];
        const copy = Object.fromEntries(fields.map(key => [key, string(data[key])]));
        for (const key of ["metrics", "details"])
            copy[key] = list(data[key]).map(item => ({ label: string(item?.label), value: string(item?.value), note: string(item?.note) }));
        for (const key of ["loot", "consumables"])
            copy[key] = list(data[key]).map(item => ({ name: string(item?.name), quantity: string(item?.quantity),
                hourly: string(item?.hourly), iconUrl: string(item?.iconUrl) }));
        return copy;
    }

    function textBlock(ctx, value, width, size = 20, lineHeight = 27, weight = 400, color = COLORS.text, align = "left") {
        ctx.font = `${weight} ${size}px ${FONT}`;
        const lines = [];
        for (const paragraph of string(value).split(/\r?\n/u)) {
            if (!paragraph.trim()) { lines.push(""); continue; }
            let line = "";
            for (const word of paragraph.trim().split(/\s+/u)) {
                const candidate = line ? `${line} ${word}` : word;
                if (ctx.measureText(candidate).width <= width) { line = candidate; continue; }
                if (line) { lines.push(line); line = ""; }
                // Long item names and unbroken numbers still remain inside their column.
                for (const character of Array.from(word)) {
                    if (line && ctx.measureText(line + character).width > width) { lines.push(line); line = ""; }
                    line += character;
                }
            }
            lines.push(line);
        }
        return { lines, width, size, lineHeight, weight, color, align, height: lines.length * lineHeight };
    }

    function drawText(ctx, block, x, y) {
        ctx.font = `${block.weight} ${block.size}px ${FONT}`;
        ctx.fillStyle = block.color;
        ctx.textAlign = block.align;
        ctx.textBaseline = "top";
        for (const line of block.lines) {
            ctx.fillText(line, x + (block.align === "right" ? block.width : 0), y);
            y += block.lineHeight;
        }
    }

    function roundedPath(ctx, x, y, width, height, radius) {
        const r = Math.min(radius, width / 2, height / 2);
        ctx.beginPath();
        ctx.moveTo(x + r, y); ctx.lineTo(x + width - r, y);
        ctx.quadraticCurveTo(x + width, y, x + width, y + r);
        ctx.lineTo(x + width, y + height - r);
        ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
        ctx.lineTo(x + r, y + height);
        ctx.quadraticCurveTo(x, y + height, x, y + height - r);
        ctx.lineTo(x, y + r); ctx.quadraticCurveTo(x, y, x + r, y); ctx.closePath();
    }

    function panel(ctx, x, y, width, height, fill = COLORS.panel, radius = 16) {
        roundedPath(ctx, x, y, width, height, radius);
        ctx.fillStyle = fill; ctx.fill();
    }

    function rule(ctx, x, y, width) {
        ctx.fillStyle = COLORS.line;
        ctx.fillRect(x, y, width, 1);
    }

    function layout(ctx, data) {
        const titleX = data.iconUrl ? PAD + 90 : PAD;
        const title = textBlock(ctx, data.title, WIDTH - PAD - titleX, 40, 48, 650);
        const subtitle = textBlock(ctx, data.subtitle, WIDTH - PAD - titleX, 20, 28, 400, COLORS.muted);
        const titleY = 92;
        const subtitleY = titleY + title.height + 12;
        const headerHeight = Math.max(212, subtitleY + subtitle.height + 30);
        const result = { title, titleX, titleY, subtitle, subtitleY, headerHeight, metrics: [], details: [], sections: [] };
        let y = headerHeight + 24;

        // The actual drops lead the image. Keep large sessions in compact columns,
        // while everyday sessions get a readable grid of item cards.
        if (data.loot.length > 0 && data.loot.length <= 24) {
            const title = textBlock(ctx, data.lootHeading, INNER, 32, 40, 700, COLORS.gold);
            result.lootTitle = { block: title, y };
            y += title.height + 20;
            const columns = data.loot.length === 1 ? 1 : 2;
            const width = (INNER - GAP * (columns - 1)) / columns;
            const quantityWidth = columns === 1 ? 220 : 148;
            const nameWidth = width - 120 - quantityWidth - 20;
            result.lootCards = [];
            for (let index = 0; index < data.loot.length; index += columns) {
                const row = data.loot.slice(index, index + columns).map(item => {
                    const name = textBlock(ctx, item.name, nameWidth, 22, 29, 600);
                    const quantity = textBlock(ctx, item.quantity, quantityWidth, 32, 40, 700, COLORS.gold, "right");
                    const hourly = item.hourly ? textBlock(ctx, `${item.hourly} / h`, nameWidth, 16, 23, 400, COLORS.muted) : null;
                    return { name, quantity, hourly, iconUrl: item.iconUrl,
                        height: Math.max(112, 40 + name.height + (hourly ? hourly.height + 8 : 0), 40 + quantity.height) };
                });
                const height = Math.max(...row.map(item => item.height));
                row.forEach((item, column) => result.lootCards.push({ ...item, x: PAD + column * (width + GAP), y, width, height }));
                y += height + GAP;
            }
        } else if (data.loot.length > 0) {
            addTable("loot", data.lootHeading, true);
        }

        y += 20;
        const metricWidth = (INNER - GAP * 3) / 4;
        for (let index = 0; index < data.metrics.length; index += 4) {
            const row = data.metrics.slice(index, index + 4).map(item => {
                const label = textBlock(ctx, item.label, metricWidth - 40, 14, 20, 600, COLORS.muted);
                const value = textBlock(ctx, item.value, metricWidth - 40, 28, 36, 600);
                const note = item.note ? textBlock(ctx, item.note, metricWidth - 40, 15, 21, 400, COLORS.muted) : null;
                return { label, value, note, height: 36 + label.height + 8 + value.height + (note ? 8 + note.height : 0) };
            });
            const height = Math.max(...row.map(item => item.height));
            row.forEach((item, column) => result.metrics.push({ ...item, x: PAD + column * (metricWidth + GAP), y, width: metricWidth, height }));
            y += height + GAP;
        }

        const detailWidth = (INNER - GAP * 2) / 3;
        for (let index = 0; index < data.details.length; index += 3) {
            const row = data.details.slice(index, index + 3).map(item => {
                const label = textBlock(ctx, item.label, detailWidth - 40, 14, 20, 600, COLORS.muted);
                const value = textBlock(ctx, item.value, detailWidth - 40, 19, 26, 500);
                const note = item.note ? textBlock(ctx, item.note, detailWidth - 40, 15, 21, 400, COLORS.muted) : null;
                return { label, value, note, height: 36 + label.height + 6 + value.height + (note ? 5 + note.height : 0) };
            });
            const height = Math.max(...row.map(item => item.height));
            row.forEach((item, column) => result.details.push({ ...item, x: PAD + column * (detailWidth + GAP), y, width: detailWidth, height }));
            y += height + 10;
        }

        if (data.consumables.length) {
            y += 22;
            addTable("consumables", data.consumablesHeading);
        }

        function addTable(key, heading, primary = false) {
            const title = textBlock(ctx, heading, INNER, primary ? 32 : 23, primary ? 40 : 31, 650, primary ? COLORS.gold : COLORS.muted);
            const columns = data[key].length > 24 ? 2 : 1;
            const width = (INNER - GAP * (columns - 1)) / columns;
            const perColumn = Math.ceil(data[key].length / columns);
            const hasHourly = data[key].some(item => item.hourly);
            const quantityWidth = columns === 2 ? 102 : 160;
            const hourlyWidth = hasHourly ? (columns === 2 ? 94 : 160) : 0;
            const nameWidth = width - 86 - quantityWidth - (hasHourly ? hourlyWidth + 18 : 0) - 20;
            const quantityLabel = textBlock(ctx, data.quantityLabel, quantityWidth, 13, 18, 600, COLORS.muted, "right");
            const hourlyLabel = hasHourly ? textBlock(ctx, data.hourlyLabel, hourlyWidth, 13, 18, 600, COLORS.muted, "right") : null;
            const headingHeight = Math.max(quantityLabel.height, hourlyLabel?.height || 0) + 24;
            const section = { title, y, width, hasHourly, quantityWidth, hourlyWidth, quantityLabel, hourlyLabel, headingHeight, columns: [] };
            for (let column = 0; column < columns; column++) {
                let rowY = y + title.height + 17 + headingHeight;
                const x = PAD + column * (width + GAP);
                const rows = data[key].slice(column * perColumn, (column + 1) * perColumn).map(item => {
                    const name = textBlock(ctx, item.name, nameWidth, 19, 25, 500);
                    const quantity = textBlock(ctx, item.quantity, quantityWidth, primary ? 22 : 19, 27, 600, primary ? COLORS.gold : COLORS.text, "right");
                    const hourly = hasHourly ? textBlock(ctx, item.hourly, hourlyWidth, 17, 25, 400, COLORS.muted, "right") : null;
                    const height = Math.max(66, name.height + 26, quantity.height + 26, (hourly?.height || 0) + 26);
                    const row = { name, quantity, hourly, iconUrl: item.iconUrl, x, y: rowY, height };
                    rowY += height;
                    return row;
                });
                section.columns.push({ x, y: y + title.height + 17, height: rowY - (y + title.height + 17), rows });
            }
            result.sections.push(section);
            y = Math.max(...section.columns.map(column => column.y + column.height));
        }

        result.footer = textBlock(ctx, data.footer, INNER, 14, 21, 400, COLORS.muted);
        result.footerY = y + 24;
        result.height = Math.ceil(result.footerY + Math.max(21, result.footer.height) + 24);
        if (result.height > MAX_HEIGHT)
            throw new Error("This session contains too much text or too many items for one PNG image.");
        return result;
    }

    function localAssetUrl(value) {
        if (!value) return null;
        try {
            const url = new URL(value, document.baseURI || window.location.href);
            return url.origin === window.location.origin && ["http:", "https:"].includes(url.protocol) ? url.href : null;
        } catch { return null; }
    }

    async function loadAssets(data) {
        const urls = [...new Set([data.backgroundUrl, data.iconUrl, ...data.loot.map(item => item.iconUrl),
            ...data.consumables.map(item => item.iconUrl)].map(localAssetUrl).filter(Boolean))];
        const images = new Map();
        await Promise.all(urls.map(url => new Promise(resolve => {
            const img = new Image();
            let finished = false;
            const finish = loaded => {
                if (finished) return;
                finished = true;
                clearTimeout(timeout);
                img.onload = img.onerror = null;
                if (loaded && img.naturalWidth > 0 && img.naturalHeight > 0) images.set(url, img);
                else img.src = "";
                resolve();
            };
            const timeout = setTimeout(() => finish(false), 3000);
            img.crossOrigin = "anonymous";
            img.onload = () => finish(true);
            img.onerror = () => finish(false);
            img.src = url;
        })));
        return value => images.get(localAssetUrl(value));
    }

    function cover(ctx, img, x, y, width, height) {
        const scale = Math.max(width / img.naturalWidth, height / img.naturalHeight);
        const sourceWidth = width / scale, sourceHeight = height / scale;
        ctx.drawImage(img, (img.naturalWidth - sourceWidth) / 2, (img.naturalHeight - sourceHeight) / 2,
            sourceWidth, sourceHeight, x, y, width, height);
    }

    function icon(ctx, img, x, y, size) {
        panel(ctx, x, y, size, size, "#233235", 10);
        if (img) {
            ctx.save(); roundedPath(ctx, x + 3, y + 3, size - 6, size - 6, 7); ctx.clip();
            const scale = Math.min((size - 6) / img.naturalWidth, (size - 6) / img.naturalHeight);
            const width = img.naturalWidth * scale, height = img.naturalHeight * scale;
            ctx.drawImage(img, x + (size - width) / 2, y + (size - height) / 2, width, height);
            ctx.restore();
        } else {
            ctx.fillStyle = COLORS.teal;
            ctx.beginPath(); ctx.moveTo(x + size / 2, y + size * .27); ctx.lineTo(x + size * .72, y + size / 2);
            ctx.lineTo(x + size / 2, y + size * .73); ctx.lineTo(x + size * .28, y + size / 2); ctx.closePath(); ctx.fill();
        }
    }

    function paint(ctx, data, plan, image) {
        ctx.fillStyle = COLORS.background; ctx.fillRect(0, 0, WIDTH, plan.height);
        const background = image(data.backgroundUrl);
        if (background) cover(ctx, background, 0, 0, WIDTH, plan.headerHeight);
        const gradient = ctx.createLinearGradient(0, 0, WIDTH * .75, plan.headerHeight);
        gradient.addColorStop(0, "rgba(11, 24, 25, .88)");
        gradient.addColorStop(.6, "rgba(16, 23, 25, .77)");
        gradient.addColorStop(1, COLORS.background);
        ctx.fillStyle = gradient; ctx.fillRect(0, 0, WIDTH, plan.headerHeight);
        ctx.fillStyle = COLORS.teal; ctx.fillRect(PAD, 41, 4, 20);
        drawText(ctx, textBlock(ctx, "GRINDCREST", 300, 19, 24, 700), PAD + 16, 38);
        if (data.iconUrl) icon(ctx, image(data.iconUrl), PAD, plan.titleY - 1, 66);
        drawText(ctx, plan.title, plan.titleX, plan.titleY);
        drawText(ctx, plan.subtitle, plan.titleX, plan.subtitleY);
        rule(ctx, PAD, plan.headerHeight - 1, INNER);

        if (plan.lootTitle) drawText(ctx, plan.lootTitle.block, PAD, plan.lootTitle.y);
        for (const item of plan.lootCards || []) {
            panel(ctx, item.x, item.y, item.width, item.height, "#35413a", 14);
            panel(ctx, item.x + 1, item.y + 1, item.width - 2, item.height - 2, "#1d2b2c", 13);
            icon(ctx, image(item.iconUrl), item.x + 20, item.y + (item.height - 64) / 2, 64);
            const textHeight = item.name.height + (item.hourly ? item.hourly.height + 8 : 0);
            const textY = item.y + (item.height - textHeight) / 2;
            drawText(ctx, item.name, item.x + 104, textY);
            if (item.hourly) drawText(ctx, item.hourly, item.x + 104, textY + item.name.height + 8);
            drawText(ctx, item.quantity, item.x + item.width - 20 - item.quantity.width,
                item.y + (item.height - item.quantity.height) / 2);
        }

        for (const metric of plan.metrics) {
            panel(ctx, metric.x, metric.y, metric.width, metric.height);
            let y = metric.y + 18;
            drawText(ctx, metric.label, metric.x + 20, y); y += metric.label.height + 8;
            drawText(ctx, metric.value, metric.x + 20, y); y += metric.value.height + 8;
            if (metric.note) drawText(ctx, metric.note, metric.x + 20, y);
        }
        for (const detail of plan.details) {
            panel(ctx, detail.x, detail.y, detail.width, detail.height, "#141e21", 10);
            let y = detail.y + 18;
            drawText(ctx, detail.label, detail.x + 20, y); y += detail.label.height + 6;
            drawText(ctx, detail.value, detail.x + 20, y); y += detail.value.height + 5;
            if (detail.note) drawText(ctx, detail.note, detail.x + 20, y);
        }
        for (const section of plan.sections) {
            drawText(ctx, section.title, PAD, section.y);
            for (const column of section.columns) {
                panel(ctx, column.x, column.y, section.width, column.height, "#152023", 12);
                const hourlyX = column.x + section.width - 20 - section.hourlyWidth;
                const quantityX = column.x + section.width - 20 - section.quantityWidth - (section.hasHourly ? section.hourlyWidth + 18 : 0);
                drawText(ctx, section.quantityLabel, quantityX, column.y + 12);
                if (section.hourlyLabel) drawText(ctx, section.hourlyLabel, hourlyX, column.y + 12);
                rule(ctx, column.x + 18, column.y + section.headingHeight - 1, section.width - 36);
                for (const row of column.rows) {
                    icon(ctx, image(row.iconUrl), row.x + 18, row.y + (row.height - 40) / 2, 40);
                    drawText(ctx, row.name, row.x + 72, row.y + (row.height - row.name.height) / 2);
                    drawText(ctx, row.quantity, quantityX, row.y + (row.height - row.quantity.height) / 2);
                    if (row.hourly) drawText(ctx, row.hourly, hourlyX, row.y + (row.height - row.hourly.height) / 2);
                    rule(ctx, row.x + 18, row.y + row.height - 1, section.width - 36);
                }
            }
        }
        rule(ctx, PAD, plan.footerY - 16, INNER);
        drawText(ctx, plan.footer, PAD, plan.footerY);
    }

    function checkPng(dataUrl) {
        if (typeof dataUrl !== "string" || !/^data:image\/png;base64,[A-Za-z0-9+/]+={0,2}$/u.test(dataUrl))
            throw new Error("No valid PNG image is available.");
    }

    window.grindcrestSessionShare = {
        async render(data) {
            const frozen = snapshot(data);
            const canvas = document.createElement("canvas");
            const ctx = canvas.getContext("2d");
            if (!ctx) throw new Error("This device cannot render session images.");
            const plan = layout(ctx, frozen);
            canvas.width = WIDTH; canvas.height = plan.height;
            const image = await loadAssets(frozen);
            paint(ctx, frozen, plan, image);
            const result = canvas.toDataURL("image/png");
            checkPng(result);
            return result;
        },
        download(dataUrl, fileName) {
            checkPng(dataUrl);
            const anchor = document.createElement("a");
            const name = string(fileName).replace(/[<>:"/\\|?*\u0000-\u001f]/gu, "-").trim() || "grindcrest-session";
            anchor.download = /\.png$/iu.test(name) ? name : `${name}.png`;
            anchor.href = dataUrl;
            anchor.style.display = "none";
            document.body.appendChild(anchor);
            try { anchor.click(); } finally { anchor.remove(); }
        },
        async copy(dataUrl) {
            checkPng(dataUrl);
            if (!navigator.clipboard?.write || typeof ClipboardItem === "undefined")
                throw new Error("Image clipboard access is unavailable on this device.");
            const bytes = Uint8Array.from(atob(dataUrl.split(",")[1]), character => character.charCodeAt(0));
            const blob = new Blob([bytes], { type: "image/png" });
            await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]);
        }
    };
})();
