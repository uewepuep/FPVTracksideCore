var scrollTimeout = 5000;
var scrollAmount = 500;

window.setTimeout(scrollDown, 500);

var lastScroll = 1;

function scrollDown() {

    let scroll = window.scrollY;
    window.scrollBy({ top: scrollAmount, behavior: 'smooth' });

    if (lastScroll === scroll) {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
    lastScroll = window.scrollY;
    window.setTimeout(scrollDown, scrollTimeout);
}