(function(){
    // Immediately-Invoked to provide a small API on window and keep global scope minimal
    function getStoredTheme(){
        try{ return localStorage.getItem('theme'); }catch(e){return null}
    }

    function detectPreferred(){
        try{
            if(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) return 'dark';
        }catch(e){}
        return 'light';
    }

    function applyTheme(theme){
        try{
            if(theme === 'dark') document.documentElement.setAttribute('data-theme','dark');
            else document.documentElement.removeAttribute('data-theme');
        }catch(e){}
    }

    function setTheme(theme){
        try{ localStorage.setItem('theme', theme); }catch(e){}
        applyTheme(theme);
    }

    function toggleTheme(){
        var cur = getStoredTheme() || (document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light');
        var next = cur === 'dark' ? 'light' : 'dark';
        setTheme(next);
        return next;
    }

    // Init on load: prefer stored value, fall back to system preference
    try{
        var stored = getStoredTheme();
        var initial = stored || detectPreferred();
        applyTheme(initial);
    }catch(e){/* ignore */}

    // Expose API
    window.themeUtils = {
        current: function(){ return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'; },
        set: setTheme,
        toggle: toggleTheme
    };

    // Helper for direct onclick handlers
    window.toggleTheme = toggleTheme;
    window.setTheme = setTheme;
})();
