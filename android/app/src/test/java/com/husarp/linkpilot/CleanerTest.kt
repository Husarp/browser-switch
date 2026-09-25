package com.husarp.linkpilot

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

// The same known answers as LinkPilot for Windows' cleaning test (CleanTest.cs), so both clean
// links the same way.
class CleanerTest {
    private val on = Cleaner.Options()
    private fun clean(link: String, o: Cleaner.Options = on) = Cleaner.apply(link, o).url

    @Test fun utmRemovedRestKeptInOrder() = assertEquals("https://example.com/page?id=5&b=2",
        clean("https://example.com/page?id=5&utm_source=x&b=2&utm_medium=y"))
    @Test fun onlyTrackingTheQuestionMarkGoesToo() = assertEquals("https://example.com/", clean("https://example.com/?fbclid=abc"))
    @Test fun fragmentKept() = assertEquals("https://example.com/a#section", clean("https://example.com/a?utm_campaign=z#section"))
    @Test fun namesInCapitals() = assertEquals("https://example.com/?k=1", clean("https://example.com/?UTM_Source=x&k=1"))
    @Test fun youtubeShortSi() = assertEquals("https://youtu.be/dQw4w9WgXcQ", clean("https://youtu.be/dQw4w9WgXcQ?si=abc123"))
    @Test fun youtubeVideoAndTimeKept() = assertEquals("https://www.youtube.com/watch?v=abc&t=42s",
        clean("https://www.youtube.com/watch?v=abc&t=42s&si=zz&pp=ygUE"))
    @Test fun siOnAnotherSiteLeftAlone() = assertEquals("https://example.com/?si=5", clean("https://example.com/?si=5"))
    @Test fun googleRedirectUrlEncoded() = assertEquals("https://github.com/x",
        clean("https://www.google.com/url?sa=t&url=https%3A%2F%2Fgithub.com%2Fx%3Futm_source%3Dg&ved=2"))
    @Test fun googleRedirectQPlain() = assertEquals("https://example.org/page", clean("https://www.google.com/url?q=https://example.org/page&sa=D"))
    @Test fun googleDocsRedirect() = assertEquals("https://example.org/doc", clean("https://docs.google.com/url?q=https%3A%2F%2Fexample.org%2Fdoc"))
    @Test fun googleSearchIsNotARedirect() = assertEquals("https://www.google.com/search?q=https://x.com",
        clean("https://www.google.com/search?q=https://x.com"))
    @Test fun outlookAroundGoogle() = assertEquals("https://example.org/deep",
        clean("https://eur01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fwww.google.com%2Furl%3Fq%3Dhttps%253A%252F%252Fexample.org%252Fdeep%26sa%3DD&data=05"))
    @Test fun facebookRedirectFbclidInside() = assertEquals("https://news.example/story",
        clean("https://l.facebook.com/l.php?u=https%3A%2F%2Fnews.example%2Fstory%3Ffbclid%3DIwAR&h=AT0"))
    @Test fun youtubeDescriptionLink() = assertEquals("https://example.org/shop",
        clean("https://www.youtube.com/redirect?event=video_description&q=https%3A%2F%2Fexample.org%2Fshop"))
    @Test fun steamLinkFilter() = assertEquals("https://example.org", clean("https://steamcommunity.com/linkfilter/?u=https%3A%2F%2Fexample.org"))
    @Test fun scriptInsideRedirectRefused() = assertEquals("https://www.google.com/url?q=javascript:alert(1)",
        clean("https://www.google.com/url?q=javascript:alert(1)"))
    @Test fun amazon() = assertEquals("https://www.amazon.de/Some-Product/dp/B000123?keywords=cable&th=1",
        clean("https://www.amazon.de/Some-Product/dp/B000123/ref=sr_1_3?crid=ABC&keywords=cable&qid=1700&sprefix=cab&sr=8-3&th=1"))
    @Test fun aliExpressShareLink() = assertEquals("https://www.aliexpress.com/item/1005006123456789.html",
        clean("https://www.aliexpress.com/item/1005006123456789.html?spm=a2g0o.productlist.main.1&algo_pvid=x&aff_fcid=1&aff_fsk=2&aff_platform=link-c-tool&sk=_d7x&aff_trace_key=k&terminal_id=t&afSmartRedirect=y&pdp_npi=4%40dis%21PLN&gatewayAdapt=glo2pol"))
    @Test fun allegroOffer() = assertEquals("https://allegro.pl/oferta/sluchawki-12345",
        clean("https://allegro.pl/oferta/sluchawki-12345?bi_s=ads&bi_m=listing%3Adesktop%3Aquery&bi_c=abc&bi_t=ape&reco_id=r1&sid=s1"))
    @Test fun allegroUnknownPartKept() = assertEquals("https://allegro.pl/oferta/x-1?snapshot=MjAy", clean("https://allegro.pl/oferta/x-1?reco_id=r&snapshot=MjAy"))
    @Test fun temuShareLink() = assertEquals("https://www.temu.com/pl/goods.html?goods_id=601099",
        clean("https://www.temu.com/pl/goods.html?_bg_fs=1&goods_id=601099&refer_page_name=home&refer_page_id=10005&_x_sessn_id=abc&share_uin=XYZ"))
    @Test fun ebayVariantKept() = assertEquals("https://www.ebay.com/itm/1234?var=7", clean("https://www.ebay.com/itm/1234?hash=item1c&_trkparms=x&mkcid=1&campid=5&var=7"))
    @Test fun amazonAffiliate() = assertEquals("https://www.amazon.pl/dp/B0ABC", clean("https://www.amazon.pl/dp/B0ABC?linkCode=ll1&linkId=abc&ascsubtag=z&smid=A1"))
    @Test fun etsyListing() = assertEquals("https://www.etsy.com/listing/123/mug",
        clean("https://www.etsy.com/listing/123/mug?click_key=a&click_sum=b&ref=hp_rv&ga_order=most_relevant"))
    @Test fun shopPartsElsewhereKept() = assertEquals("https://example.com/?sk=1&ref=x", clean("https://example.com/?sk=1&ref=x"))
    @Test fun notAWebLinkUntouched() = assertEquals("content://x/page.htm?utm_source=x", clean("content://x/page.htm?utm_source=x"))

    @Test fun siSwitchedOff() = assertEquals("https://youtu.be/x?si=abc",
        clean("https://youtu.be/x?si=abc", Cleaner.Options(partsOff = setOf("si@youtube.com youtu.be open.spotify.com"))))
    @Test fun trackingOffRedirectStillSkipped() = assertEquals("https://example.org/?utm_source=x",
        clean("https://www.google.com/url?q=https://example.org/?utm_source=x", Cleaner.Options(cleanOn = false)))
    @Test fun redirectsOffOwnTrackingRemoved() = assertEquals("https://www.google.com/url?q=https://example.org/",
        clean("https://www.google.com/url?q=https://example.org/&utm_source=x", Cleaner.Options(unwrapOn = false)))
    @Test fun googleRedirectSwitchedOff() = assertEquals("https://www.google.com/url?q=https://example.org/",
        clean("https://www.google.com/url?q=https://example.org/", Cleaner.Options(redirectsOff = setOf("google.*/url"))))

    @Test fun whatWasChanged() = assertEquals("skipped Google redirect; removed utm_source",
        Cleaner.apply("https://www.google.com/url?q=https%3A%2F%2Fexample.org%2Fa%3Futm_source%3Dx%26id%3D7", on).changes)
    @Test fun plusSignKept() = assertEquals("https://example.org/?q=a+b",
        clean("https://www.google.com/url?q=https%3A%2F%2Fexample.org%2F%3Fq%3Da%2Bb"))

    @Test fun addressRules() {
        assertTrue(Router.hasAddress("github.com", "https://gist.github.com/x"))
        assertTrue(Router.hasAddress("github.com", "https://github.com/"))
        assertFalse(Router.hasAddress("github.com", "https://notgithub.com/"))
        assertTrue(Router.hasAddress("github.com/my-company", "https://github.com/my-company/repo"))
        assertFalse(Router.hasAddress("github.com/my-company", "https://github.com/other"))
        assertEquals("github.com", Router.cleanAddress(" https://github.com/ "))
    }
}
