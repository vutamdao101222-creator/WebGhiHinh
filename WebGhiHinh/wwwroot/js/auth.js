window.auth = {
    get: function () {
        return localStorage.getItem("token");
    },
    set: function (token) {
        localStorage.setItem("token", token);
    },
    clear: function () {
        localStorage.clear();
    }
};
