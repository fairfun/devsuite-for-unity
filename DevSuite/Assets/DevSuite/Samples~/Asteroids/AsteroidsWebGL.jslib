mergeInto(LibraryManager.library, {
    SetCanvasSizeWebGL: function (width, height) {
        var canvas = document.querySelector("#unity-canvas");
        if (canvas) {
            canvas.style.width = width + "px";
            canvas.style.height = height + "px";
            window.dispatchEvent(new Event('resize'));
        }
    },

});
