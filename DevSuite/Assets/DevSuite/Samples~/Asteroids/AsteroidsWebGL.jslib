mergeInto(LibraryManager.library, {
    SetCanvasSizeWebGL: function (width, height) {
        var canvas = document.querySelector("#unity-canvas");
        if (canvas) {
            canvas.style.width = width + "px";
            canvas.style.height = height + "px";
            window.dispatchEvent(new Event('resize'));
        }
    },

    SetDemoSubtitleWebGL: function (textPtr) {
        var text = UTF8ToString(textPtr);
        var inputId = "devsuite-demo-subtitle-input";
        var input = document.getElementById(inputId);

        if (!input) {
            var canvas = document.querySelector("#unity-canvas");
            if (!canvas) return;

            var container = document.createElement("div");
            container.id = "devsuite-demo-subtitle-container";
            container.style.width = "100%";
            container.style.maxWidth = "1536px";
            container.style.margin = "10px auto 0 auto";
            container.style.padding = "0 8px";
            container.style.boxSizing = "border-box";
            container.style.display = "flex";
            container.style.justifyContent = "center";
            container.style.zIndex = "999";

            input = document.createElement("input");
            input.id = inputId;
            input.type = "text";
            input.readOnly = true;
            input.style.width = "100%";
            input.style.padding = "10px 16px";
            input.style.fontSize = "15px";
            input.style.fontWeight = "600";
            input.style.fontFamily = "-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
            input.style.color = "#ffcc00";
            input.style.backgroundColor = "transparent";
            input.style.border = "none";
            input.style.borderRadius = "0";
            input.style.outline = "none";
            input.style.boxShadow = "none";
            input.style.textAlign = "center";
            input.style.transition = "all 0.25s ease";

            container.appendChild(input);

            if (canvas.nextSibling) {
                canvas.parentNode.insertBefore(container, canvas.nextSibling);
            } else {
                canvas.parentNode.appendChild(container);
            }
        }

        input.value = text;
        if (text && text.trim().length > 0) {
            input.style.display = "block";
            input.parentElement.style.display = "flex";
        } else {
            input.parentElement.style.display = "none";
        }
    }
});
