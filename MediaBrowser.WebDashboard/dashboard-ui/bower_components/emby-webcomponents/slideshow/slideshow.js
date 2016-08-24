define(['dialogHelper', 'inputManager', 'connectionManager', 'layoutManager', 'focusManager', 'apphost', 'css!./style', 'html!./icons', 'iron-icon-set', 'paper-icon-button', 'paper-spinner'], function (dialogHelper, inputmanager, connectionManager, layoutManager, focusManager, appHost) {

    function getImageUrl(item, options, apiClient) {

        options = options || {};
        options.type = options.type || "Primary";

        if (typeof (item) === 'string') {
            return apiClient.getScaledImageUrl(item, options);
        }

        if (item.ImageTags && item.ImageTags[options.type]) {

            options.tag = item.ImageTags[options.type];
            return apiClient.getScaledImageUrl(item.Id, options);
        }

        if (options.type == 'Primary') {
            if (item.AlbumId && item.AlbumPrimaryImageTag) {

                options.tag = item.AlbumPrimaryImageTag;
                return apiClient.getScaledImageUrl(item.AlbumId, options);
            }

            //else if (item.AlbumId && item.SeriesPrimaryImageTag) {

            //    imgUrl = ApiClient.getScaledImageUrl(item.SeriesId, {
            //        type: "Primary",
            //        width: downloadWidth,
            //        tag: item.SeriesPrimaryImageTag,
            //        minScale: minScale
            //    });

            //}
            //else if (item.ParentPrimaryImageTag) {

            //    imgUrl = ApiClient.getImageUrl(item.ParentPrimaryImageItemId, {
            //        type: "Primary",
            //        width: downloadWidth,
            //        tag: item.ParentPrimaryImageTag,
            //        minScale: minScale
            //    });
            //}
        }

        return null;
    }

    function getBackdropImageUrl(item, options, apiClient) {

        options = options || {};
        options.type = options.type || "Backdrop";

        options.width = null;
        delete options.width;
        options.maxWidth = null;
        delete options.maxWidth;
        options.maxHeight = null;
        delete options.maxHeight;
        options.height = null;
        delete options.height;

        // If not resizing, get the original image
        if (!options.maxWidth && !options.width && !options.maxHeight && !options.height) {
            options.quality = 100;
        }

        if (item.BackdropImageTags && item.BackdropImageTags.length) {

            options.tag = item.BackdropImageTags[0];
            return apiClient.getScaledImageUrl(item.Id, options);
        }

        return null;
    }

    function getImgUrl(item, original) {

        var apiClient = connectionManager.getApiClient(item.ServerId);
        var imageOptions = {};

        if (!original) {
            imageOptions.maxWidth = screen.availWidth;
        }
        if (item.BackdropImageTags && item.BackdropImageTags.length) {
            return getBackdropImageUrl(item, imageOptions, apiClient);
        } else {

            if (item.MediaType == 'Photo' && original) {
                return apiClient.getUrl("Items/" + item.Id + "/Download", {
                    api_key: apiClient.accessToken()
                });
            }
            imageOptions.type = "Primary";
            return getImageUrl(item, imageOptions, apiClient);
        }
    }

    return function (options) {

        var self = this;
        var swiperInstance;
        var dlg;
        var currentTimeout;
        var currentIntervalMs;
        var currentOptions;
        var currentIndex;

        function createElements(options) {

            dlg = dialogHelper.createDialog({
                exitAnimationDuration: options.interactive ? 400 : 800,
                size: 'fullscreen',
                autoFocus: false
            });

            dlg.classList.add('slideshowDialog');

            var html = '';

            if (options.interactive) {

                html += '<div>';
                html += '<div class="slideshowSwiperContainer"><div class="swiper-wrapper"></div></div>';

                html += '<paper-icon-button icon="slideshow:keyboard-arrow-left" class="btnSlideshowPrevious slideshowButton" tabindex="-1"></paper-icon-button>';
                html += '<paper-icon-button icon="slideshow:keyboard-arrow-right" class="btnSlideshowNext slideshowButton" tabindex="-1"></paper-icon-button>';

                html += '<paper-icon-button icon="slideshow:close" class="btnSlideshowExit" tabindex="-1"></paper-icon-button>';

                html += '<div class="slideshowBottomBar hide">';

                //html += '<paper-icon-button icon="slideshow:share" class="btnShare slideshowButton"></paper-icon-button>';
                html += '<paper-icon-button icon="slideshow:pause" class="btnSlideshowPause slideshowButton" autoFocus></paper-icon-button>';
                if (appHost.supports('filedownload')) {
                    html += '<paper-icon-button icon="slideshow:file-download" class="btnDownload slideshowButton"></paper-icon-button>';
                }

                html += '</div>';

                html += '</div>';

            } else {
                html += '<div class="slideshowImage"></div><h1 class="slideshowImageText"></h1>';
            }

            dlg.innerHTML = html;

            if (options.interactive) {
                dlg.querySelector('.btnSlideshowExit').addEventListener('click', function (e) {

                    dialogHelper.close(dlg);
                });
                dlg.querySelector('.btnSlideshowNext').addEventListener('click', nextImage);
                dlg.querySelector('.btnSlideshowPrevious').addEventListener('click', previousImage);

                var btnPause = dlg.querySelector('.btnSlideshowPause');
                if (btnPause) {
                    btnPause.addEventListener('click', playPause);
                }

                var btnDownload = dlg.querySelector('.btnDownload');
                if (btnDownload) {
                    btnDownload.addEventListener('click', download);
                }

                var btnShare = dlg.querySelector('.btnShare');
                if (btnShare) {
                    btnShare.addEventListener('click', share);
                }
            }

            document.body.appendChild(dlg);

            dialogHelper.open(dlg).then(function () {

                stopInterval();
                dlg.parentNode.removeChild(dlg);
            });

            inputmanager.on(window, onInputCommand);
            document.addEventListener('mousemove', onMouseMove);

            dlg.addEventListener('close', onDialogClosed);

            if (options.interactive) {
                loadSwiper(dlg);
            }
        }

        function loadSwiper(dlg) {

            if (currentOptions.slides) {
                dlg.querySelector('.swiper-wrapper').innerHTML = currentOptions.slides.map(getSwiperSlideHtmlFromSlide).join('');
            } else {
                dlg.querySelector('.swiper-wrapper').innerHTML = currentOptions.items.map(getSwiperSlideHtmlFromItem).join('');
            }

            require(['swiper'], function (swiper) {

                swiperInstance = new Swiper(dlg.querySelector('.slideshowSwiperContainer'), {
                    // Optional parameters
                    direction: 'horizontal',
                    loop: options.loop !== false,
                    autoplay: options.interval || 8000,
                    // Disable preloading of all images
                    preloadImages: false,
                    // Enable lazy loading
                    lazyLoading: true,
                    autoplayDisableOnInteraction: false,
                    initialSlide: options.startIndex || 0
                });

                swiperInstance.on('onLazyImageLoad', onSlideChangeStart);
                swiperInstance.on('onLazyImageReady', onSlideChangeEnd);

                if (layoutManager.mobile) {
                    pause();
                } else {
                    play();
                }
            });
        }

        function getSwiperSlideHtmlFromItem(item) {

            return getSwiperSlideHtmlFromSlide({
                imageUrl: getImgUrl(item),
                originalImage: getImgUrl(item, true)
                //title: item.Name,
                //description: item.Overview
            });
        }

        function onSlideChangeStart(swiper, slide, image) {

            var spinner = slide.querySelector('paper-spinner');
            if (spinner) {
                spinner.active = true;
            }
        }

        function onSlideChangeEnd(swiper, slide, image) {

            var spinner = slide.querySelector('paper-spinner');
            if (spinner) {
                spinner.active = false;
                // Remove it because in IE it might just keep in spinning forever
                spinner.parentNode.removeChild(spinner);
            }
        }

        function getSwiperSlideHtmlFromSlide(item) {

            var html = '';
            html += '<div class="swiper-slide" data-original="' + item.originalImage + '">';
            html += '<img data-src="' + item.imageUrl + '" class="swiper-lazy">';
            html += '<paper-spinner></paper-spinner>';
            if (item.title || item.subtitle) {
                html += '<div class="slideText">';
                html += '<div class="slideTextInner">';
                if (item.title) {
                    html += '<div class="slideTitle">';
                    html += item.title;
                    html += '</div>';
                }
                if (item.description) {
                    html += '<div class="slideSubtitle">';
                    html += item.description;
                    html += '</div>';
                }
                html += '</div>';
                html += '</div>';
            }
            html += '</div>';

            return html;
        }

        function previousImage() {
            if (swiperInstance) {
                swiperInstance.slidePrev();
            } else {
                stopInterval();
                showNextImage(currentIndex - 1);
            }
        }

        function nextImage() {
            if (swiperInstance) {

                if (options.loop === false) {

                    if (swiperInstance.activeIndex >= swiperInstance.slides.length - 1) {
                        dialogHelper.close(dlg);
                        return;
                    }
                }

                swiperInstance.slideNext();
            } else {
                stopInterval();
                showNextImage(currentIndex + 1);
            }
        }

        function getCurrentImageUrl() {


            if (swiperInstance) {
                return document.querySelector('.swiper-slide-active').getAttribute('data-original');
            } else {
                return null;
            }
        }

        function download() {

            var url = getCurrentImageUrl();

            require(['fileDownloader'], function (fileDownloader) {
                fileDownloader.download([
                {
                    url: url
                }]);
            });
        }

        function share() {

        }

        function play() {

            dlg.querySelector('.btnSlideshowPause').icon = "slideshow:pause";
            swiperInstance.startAutoplay();
        }

        function pause() {

            dlg.querySelector('.btnSlideshowPause').icon = "slideshow:play-arrow";
            swiperInstance.stopAutoplay();
        }

        function playPause() {

            var paused = dlg.querySelector('.btnSlideshowPause').icon != "slideshow:pause";
            if (paused) {
                play();
            } else {
                pause();
            }
        }

        function onDialogClosed() {

            var swiper = swiperInstance;
            if (swiper) {
                swiper.off('onLazyImageLoad');
                swiper.off('onLazyImageReady');
                swiper.destroy(true, true);
                swiperInstance = null;
            }

            inputmanager.off(window, onInputCommand);
            document.removeEventListener('mousemove', onMouseMove);
        }

        function startInterval(options) {

            currentOptions = options;

            stopInterval();
            createElements(options);

            if (!options.interactive) {
                currentIntervalMs = options.interval || 8000;
                showNextImage(options.startIndex || 0, true);
            }
        }

        var _osdOpen = false;
        function isOsdOpen() {
            return _osdOpen;
        }

        function getOsdBottom() {
            return dlg.querySelector('.slideshowBottomBar');
        }

        function showOsd() {

            slideUpToShow(getOsdBottom());
            startHideTimer();
        }

        function hideOsd() {

            slideDownToHide(getOsdBottom());
        }

        var hideTimeout;
        function startHideTimer() {
            stopHideTimer();
            hideTimeout = setTimeout(hideOsd, 4000);
        }
        function stopHideTimer() {
            if (hideTimeout) {
                clearTimeout(hideTimeout);
                hideTimeout = null;
            }
        }

        function slideUpToShow(elem) {

            if (!elem.classList.contains('hide')) {
                return;
            }

            _osdOpen = true;
            elem.classList.remove('hide');

            requestAnimationFrame(function () {

                var keyframes = [
                  { transform: 'translate3d(0,' + elem.offsetHeight + 'px,0)', opacity: '.3', offset: 0 },
                  { transform: 'translate3d(0,0,0)', opacity: '1', offset: 1 }];
                var timing = { duration: 300, iterations: 1, easing: 'ease-out' };
                elem.animate(keyframes, timing).onfinish = function () {
                    focusManager.focus(elem.querySelector('.btnSlideshowPause'));
                };
            });
        }

        function slideDownToHide(elem) {

            if (elem.classList.contains('hide')) {
                return;
            }

            requestAnimationFrame(function () {

                var keyframes = [
                  { transform: 'translate3d(0,0,0)', opacity: '1', offset: 0 },
                  { transform: 'translate3d(0,' + elem.offsetHeight + 'px,0)', opacity: '.3', offset: 1 }];
                var timing = { duration: 300, iterations: 1, easing: 'ease-out' };
                elem.animate(keyframes, timing).onfinish = function () {
                    elem.classList.add('hide');
                    _osdOpen = false;
                };
            });
        }

        var lastMouseMoveData;
        function onMouseMove(e) {

            var eventX = e.screenX || 0;
            var eventY = e.screenY || 0;

            var obj = lastMouseMoveData;
            if (!obj) {
                lastMouseMoveData = {
                    x: eventX,
                    y: eventY
                };
                return;
            }

            // if coord are same, it didn't move
            if (Math.abs(eventX - obj.x) < 10 && Math.abs(eventY - obj.y) < 10) {
                return;
            }

            obj.x = eventX;
            obj.y = eventY;

            showOsd();
        }

        function onInputCommand(e) {

            switch (e.detail.command) {

                case 'left':
                    if (!isOsdOpen()) {
                        e.preventDefault();
                        previousImage();
                    }
                    break;
                case 'right':
                    if (!isOsdOpen()) {
                        e.preventDefault();
                        nextImage();
                    }
                    break;
                case 'up':
                case 'down':
                case 'select':
                case 'menu':
                case 'info':
                case 'play':
                case 'playpause':
                case 'pause':
                case 'fastforward':
                case 'rewind':
                case 'next':
                case 'previous':
                    showOsd();
                    break;
                default:
                    break;
            }
        }

        function showNextImage(index, skipPreload) {

            index = Math.max(0, index);
            if (index >= currentOptions.items.length) {
                index = 0;
            }
            currentIndex = index;

            var options = currentOptions;
            var items = options.items;
            var item = items[index];
            var imgUrl = getImgUrl(item);

            var onSrcLoaded = function () {
                var cardImageContainer = dlg.querySelector('.slideshowImage');

                var newCardImageContainer = document.createElement('div');
                newCardImageContainer.className = cardImageContainer.className;

                if (options.cover) {
                    newCardImageContainer.classList.add('cover');
                }

                newCardImageContainer.style.backgroundImage = "url('" + imgUrl + "')";
                newCardImageContainer.classList.add('hide');
                cardImageContainer.parentNode.appendChild(newCardImageContainer);

                if (options.showTitle) {
                    dlg.querySelector('.slideshowImageText').innerHTML = item.Name;
                } else {
                    dlg.querySelector('.slideshowImageText').innerHTML = '';
                }

                newCardImageContainer.classList.remove('hide');
                var onAnimationFinished = function () {

                    var parentNode = cardImageContainer.parentNode;
                    if (parentNode) {
                        parentNode.removeChild(cardImageContainer);
                    }
                };

                if (newCardImageContainer.animate) {

                    var keyframes = [
                            { opacity: '0', offset: 0 },
                            { opacity: '1', offset: 1 }];
                    var timing = { duration: 1200, iterations: 1 };
                    newCardImageContainer.animate(keyframes, timing).onfinish = onAnimationFinished;
                } else {
                    onAnimationFinished();
                }

                stopInterval();
                currentTimeout = setTimeout(function () {
                    showNextImage(index + 1, true);

                }, currentIntervalMs);
            };

            if (!skipPreload) {
                var img = new Image();
                img.onload = onSrcLoaded;
                img.src = imgUrl;
            } else {
                onSrcLoaded();
            }
        }

        function stopInterval() {
            if (currentTimeout) {
                clearTimeout(currentTimeout);
                currentTimeout = null;
            }
        }

        self.show = function () {
            startInterval(options);
        };

        self.hide = function () {

            var dialog = dlg;
            if (dialog) {

                dialogHelper.close(dialog);
            }
        };
    }
});