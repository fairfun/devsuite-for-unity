mergeInto(LibraryManager.library, {
    CopyToClipboardWebGL: function (textPtr) {
        var text = UTF8ToString(textPtr);
        
        function fallbackCopyToClipboard(val) {
            var textArea = document.createElement("textarea");
            textArea.value = val;
            
            textArea.style.top = "0";
            textArea.style.left = "0";
            textArea.style.position = "fixed";
            textArea.style.opacity = "0";
            textArea.style.width = "2em";
            textArea.style.height = "2em";
            textArea.style.padding = "0";
            textArea.style.border = "none";
            textArea.style.outline = "none";
            textArea.style.boxShadow = "none";
            textArea.style.background = "transparent";
            
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            
            try {
                var successful = document.execCommand('copy');
                if (!successful) {
                    console.warn('[DevSuite] Fallback copy command was unsuccessful');
                }
            } catch (err) {
                console.error('[DevSuite] Fallback copy command failed', err);
            }
            
            document.body.removeChild(textArea);
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(function() {
                // Success
            }).catch(function(err) {
                fallbackCopyToClipboard(text);
            });
        } else {
            fallbackCopyToClipboard(text);
        }
    }
});
