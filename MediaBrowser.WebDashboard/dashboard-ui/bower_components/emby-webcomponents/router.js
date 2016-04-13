define(['loading', 'viewManager', 'skinManager', 'pluginManager', 'backdrop', 'browser', 'pageJs'], function (loading, viewManager, skinManager, pluginManager, backdrop, browser, page) {

    var embyRouter = {
        showLocalLogin: function (apiClient, serverId, manualLogin) {

            var pageName = manualLogin ? 'manuallogin' : 'login';

            show('/startup/' + pageName + '.html?serverid=' + serverId);
        },
        showSelectServer: function () {
            show('/startup/selectserver.html');
        },
        showWelcome: function () {
            show('/startup/welcome.html');
        },
        showSettings: function () {
            show('/settings/settings.html');
        },
        showSearch: function () {
            skinManager.getCurrentSkin().search();
        }
    };

    var connectionManager;

    function beginConnectionWizard() {

        backdrop.clear();

        loading.show();

        connectionManager.connect().then(function (result) {
            handleConnectionResult(result, loading);
        });
    }

    function handleConnectionResult(result, loading) {

        switch (result.State) {

            case MediaBrowser.ConnectionState.SignedIn:
                {
                    loading.hide();
                    skinManager.loadUserSkin();
                }
                break;
            case MediaBrowser.ConnectionState.ServerSignIn:
                {
                    result.ApiClient.getPublicUsers().then(function (users) {

                        if (users.length) {
                            embyRouter.showLocalLogin(result.ApiClient, result.Servers[0].Id);
                        } else {
                            embyRouter.showLocalLogin(result.ApiClient, result.Servers[0].Id, true);
                        }
                    });
                }
                break;
            case MediaBrowser.ConnectionState.ServerSelection:
                {
                    embyRouter.showSelectServer();
                }
                break;
            case MediaBrowser.ConnectionState.ConnectSignIn:
                {
                    embyRouter.showWelcome();
                }
                break;
            case MediaBrowser.ConnectionState.ServerUpdateNeeded:
                {
                    require(['alert'], function (alert) {
                        alert({

                            text: Globalize.translate('core#ServerUpdateNeeded', 'https://emby.media'),
                            html: Globalize.translate('core#ServerUpdateNeeded', '<a href="https://emby.media">https://emby.media</a>')

                        }).then(function () {
                            embyRouter.showSelectServer();
                        });
                    });
                }
                break;
            default:
                break;
        }
    }

    var htmlCache = {};
    var cacheParam = new Date().getTime();
    function loadContentUrl(ctx, next, route, request) {

        var url = route.contentPath || route.path;

        if (url.toLowerCase().indexOf('http') != 0 && url.indexOf('file:') != 0) {

            // Put a slash at the beginning but make sure to avoid a double slash
            if (url.indexOf('/') != 0) {

                url = '/' + url;
            }

            url = baseUrl() + url;
        }

        if (ctx.querystring && route.enableContentQueryString) {
            url += '?' + ctx.querystring;
        }

        if (route.enableCache !== false) {
            var cachedHtml = htmlCache[url];
            if (cachedHtml) {
                loadContent(ctx, route, cachedHtml, request);
                return;
            }
        }

        url += url.indexOf('?') == -1 ? '?' : '&';
        url += 'v=' + cacheParam;

        var xhr = new XMLHttpRequest();
        xhr.onload = xhr.onerror = function () {
            if (this.status < 400) {
                var html = this.response;
                if (route.enableCache !== false) {
                    htmlCache[url.split('?')[0]] = html;
                }
                loadContent(ctx, route, html, request);
            } else {
                next();
            }
        };
        xhr.onerror = next;
        xhr.open('GET', url, true);
        xhr.send();
    }

    function handleRoute(ctx, next, route) {

        authenticate(ctx, route, function () {
            initRoute(ctx, next, route);
        });
    }

    function initRoute(ctx, next, route) {

        var onInitComplete = function (controllerFactory) {
            sendRouteToViewManager(ctx, next, route, controllerFactory);
        };

        require(route.dependencies || [], function () {

            if (route.controller) {
                require([route.controller], onInitComplete);
            } else {
                onInitComplete();
            }
        });
    }

    function cancelCurrentLoadRequest() {
        var currentRequest = currentViewLoadRequest;
        if (currentRequest) {
            currentRequest.cancel = true;
        }
    }

    var currentViewLoadRequest;
    function sendRouteToViewManager(ctx, next, route, controllerFactory) {

        cancelCurrentLoadRequest();

        var isBackNav = ctx.isBack;

        var currentRequest = {
            url: baseUrl() + ctx.path,
            transition: route.transition,
            isBack: isBackNav,
            state: ctx.state,
            type: route.type,
            controllerFactory: controllerFactory,
            options: {
                supportsThemeMedia: route.supportsThemeMedia || false
            },
            autoFocus: route.autoFocus
        };
        currentViewLoadRequest = currentRequest;

        var onNewViewNeeded = function () {
            if (typeof route.path === 'string') {

                loadContentUrl(ctx, next, route, currentRequest);

            } else {
                // ? TODO
                next();
            }
        };

        if (!isBackNav) {
            // Don't force a new view for home due to the back menu
            if (route.type != 'home') {
                onNewViewNeeded();
                return;
            }
        }

        viewManager.tryRestoreView(currentRequest).then(function () {

            // done
            currentRouteInfo = {
                route: route,
                path: ctx.path
            };

        }, onNewViewNeeded);
    }

    var firstConnectionResult;
    function start(options) {

        loading.show();

        require(['connectionManager'], function (connectionManagerInstance) {

            connectionManager = connectionManagerInstance;

            connectionManager.connect().then(function (result) {

                firstConnectionResult = result;

                loading.hide();

                options = options || {};

                page({
                    click: options.click !== false,
                    hashbang: options.hashbang !== false,
                    enableHistory: enableHistory()
                });
            });
        });
    }

    function enableHistory() {

        if (browser.xboxOne) {
            return false;
        }

        return true;
    }

    function enableNativeHistory() {
        return page.enableNativeHistory();
    }

    function authenticate(ctx, route, callback) {

        var firstResult = firstConnectionResult;
        if (firstResult) {

            firstConnectionResult = null;

            if (firstResult.State != MediaBrowser.ConnectionState.SignedIn && !route.anonymous) {

                handleConnectionResult(firstResult, loading);
                return;
            }
        }

        var apiClient = connectionManager.currentApiClient();
        var pathname = ctx.pathname.toLowerCase();

        console.log('Emby.Page - processing path request ' + pathname);

        if ((!apiClient || !apiClient.isLoggedIn()) && !route.anonymous) {
            console.log('Emby.Page - route does not allow anonymous access, redirecting to login');
            beginConnectionWizard();
            return;
        }

        if (apiClient && apiClient.isLoggedIn()) {

            console.log('Emby.Page - user is authenticated');

            var isCurrentRouteStartup = currentRouteInfo ? currentRouteInfo.route.startup : true;
            if (ctx.isBack && (route.isDefaultRoute || route.startup) && !isCurrentRouteStartup) {
                handleBackToDefault();
                return;
            }
            else if (route.isDefaultRoute) {
                console.log('Emby.Page - loading skin home page');
                skinManager.loadUserSkin();
                return;
            } else if (route.roles) {
                validateRoles(apiClient, route.roles).then(function () {

                    apiClient.ensureWebSocket();
                    callback();

                }, beginConnectionWizard);
                return;
            }
        }

        console.log('Emby.Page - proceeding to ' + pathname);
        callback();
    }

    function validateRoles(apiClient, roles) {

        return Promise.all(roles.split(',').map(function (role) {
            return validateRole(apiClient, role);
        }));
    }

    function validateRole(apiClient, role) {

        if (role == 'admin') {

            return apiClient.getCurrentUser().then(function (user) {
                if (user.Policy.IsAdministrator) {
                    return Promise.resolve();
                }
                return Promise.reject();
            });
        }

        // Unknown role
        return Promise.resolve();
    }

    var isHandlingBackToDefault;
    function handleBackToDefault() {

        skinManager.loadUserSkin();

        if (isHandlingBackToDefault) {
            return;
        }

        isHandlingBackToDefault = true;

        // This must result in a call to either 
        // skinManager.loadUserSkin();
        // Logout
        // Or exit app

        skinManager.getCurrentSkin().showBackMenu().then(function () {

            isHandlingBackToDefault = false;
        });
    }

    function loadContent(ctx, route, html, request) {

        html = Globalize.translateDocument(html, route.dictionary);
        request.view = html;

        viewManager.loadView(request);

        currentRouteInfo = {
            route: route,
            path: ctx.path
        };
        //next();

        ctx.handled = true;
    }

    function getRequestFile() {
        var path = window.location.pathname || '';

        var index = path.lastIndexOf('/');
        if (index != -1) {
            path = path.substring(index);
        } else {
            path = '/' + path;
        }

        if (!path || path == '/') {
            path = '/index.html';
        }

        return path;
    }

    var baseRoute = window.location.href.split('?')[0].replace(getRequestFile(), '');
    // support hashbang
    baseRoute = baseRoute.split('#')[0];
    if (baseRoute.lastIndexOf('/') == baseRoute.length - 1) {
        baseRoute = baseRoute.substring(0, baseRoute.length - 1);
    }
    function baseUrl() {
        return baseRoute;
    }

    function getHandler(route) {
        return function (ctx, next) {
            handleRoute(ctx, next, route);
        };
    }

    function getWindowLocationSearch(win) {

        var currentPath = currentRouteInfo ? (currentRouteInfo.path || '') : '';

        var index = currentPath.indexOf('?');
        var search = '';

        if (index != -1) {
            search = currentPath.substring(index);
        }

        return search || '';
    }

    function param(name, url) {
        name = name.replace(/[\[]/, "\\\[").replace(/[\]]/, "\\\]");
        var regexS = "[\\?&]" + name + "=([^&#]*)";
        var regex = new RegExp(regexS, "i");

        var results = regex.exec(url || getWindowLocationSearch());
        if (results == null)
            return "";
        else
            return decodeURIComponent(results[1].replace(/\+/g, " "));
    }

    function back() {

        page.back();
    }
    function canGoBack() {

        var curr = current();

        if (!curr) {
            return false;
        }

        if (curr.type == 'home') {
            return false;
        }
        return page.canGoBack();
    }
    function show(path, options) {

        return new Promise(function (resolve, reject) {

            var baseRoute = baseUrl();
            path = path.replace(baseRoute, '');

            if (currentRouteInfo && currentRouteInfo.path == path) {

                // can't use this with home right now due to the back menu
                if (currentRouteInfo.route.type != 'home') {
                    resolve();
                    return;
                }
            }

            page.show(path, options);
            setTimeout(resolve, 500);
        });
    }

    var currentRouteInfo;
    function current() {
        return currentRouteInfo ? currentRouteInfo.route : null;
    }

    function goHome() {

        var skin = skinManager.getCurrentSkin();

        var homeRoute = skin.getRoutes().filter(function (r) {
            return r.type == 'home';
        })[0];

        return show(pluginManager.mapRoute(skin, homeRoute));
    }

    function showItem(item) {

        if (typeof (item) === 'string') {
            Emby.Models.item(item).then(showItem);

        } else {
            skinManager.getCurrentSkin().showItem(item);
        }
    }

    function setTitle(title) {
        skinManager.getCurrentSkin().setTitle(title);
    }

    function showVideoOsd() {
        var skin = skinManager.getCurrentSkin();

        var homeRoute = skin.getRoutes().filter(function (r) {
            return r.type == 'video-osd';
        })[0];

        return show(pluginManager.mapRoute(skin, homeRoute));
    }

    var allRoutes = [];

    function addRoute(path, newRoute) {

        page(path, getHandler(newRoute));
        allRoutes.push(newRoute);
    }

    function getRoutes() {
        return allRoutes;
    }

    function setTransparency(level) {

        if (level == 'full' || level == Emby.TransparencyLevel.Full) {
            backdrop.clear(true);
            document.documentElement.classList.add('transparentDocument');
        }
        else if (level == 'backdrop' || level == Emby.TransparencyLevel.Backdrop) {
            backdrop.externalBackdrop(true);
            document.documentElement.classList.add('transparentDocument');
        } else {
            backdrop.externalBackdrop(false);
            document.documentElement.classList.remove('transparentDocument');
        }
    }

    function pushState(state, title, url) {

        state.navigate = false;

        page.pushState(state, title, url);
    }

    function setBaseRoute() {
        var baseRoute = window.location.pathname.replace(getRequestFile(), '');
        if (baseRoute.lastIndexOf('/') == baseRoute.length - 1) {
            baseRoute = baseRoute.substring(0, baseRoute.length - 1);
        }

        console.log('Setting page base to ' + baseRoute);

        page.base(baseRoute);
    }

    setBaseRoute();

    embyRouter.addRoute = addRoute;
    embyRouter.param = param;
    embyRouter.back = back;
    embyRouter.show = show;
    embyRouter.start = start;
    embyRouter.baseUrl = baseUrl;
    embyRouter.canGoBack = canGoBack;
    embyRouter.current = current;
    embyRouter.beginConnectionWizard = beginConnectionWizard;
    embyRouter.goHome = goHome;
    embyRouter.showItem = showItem;
    embyRouter.setTitle = setTitle;
    embyRouter.setTransparency = setTransparency;
    embyRouter.getRoutes = getRoutes;
    embyRouter.pushState = pushState;
    embyRouter.enableNativeHistory = enableNativeHistory;
    embyRouter.showVideoOsd = showVideoOsd;
    embyRouter.TransparencyLevel = {
        None: 0,
        Backdrop: 1,
        Full: 2
    };

    return embyRouter;
});