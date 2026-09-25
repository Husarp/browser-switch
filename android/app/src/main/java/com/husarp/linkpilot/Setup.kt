package com.husarp.linkpilot

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

// The setup screen - the same steps as LinkPilot for Windows, fitted to a phone. It shows on
// the first start, and again whenever LinkPilot is not the default browser.
//
//   1  How it works: why it has to be the default browser, and why it can be trusted.
//   2  Your choices: link cleaning, the link log.
//   3  Make it the default - Android's own question - or "already done". Coming back from it as the
//      default browser goes on by itself.
//   4  You're all set, and what next.
//
// Back and Skip on the left, the button that goes on on the right.
@Composable
fun SetupScreen(m: Model, makeDefault: () -> Unit, openSettings: () -> Unit, finish: () -> Unit) {
    m.tick
    val ctx = LocalContext.current
    val store = m.store
    var step by rememberSaveable { mutableIntStateOf(1) }
    val isDefault = m.isDefault

    // step 3 notices by itself when Android's question was answered with LinkPilot
    LaunchedEffect(isDefault, step) { if (step == 3 && isDefault && m.askedForDefault) step = 4 }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(20.dp),
           verticalArrangement = Arrangement.spacedBy(12.dp)) {
        when (step) {
            1 -> {
                Heading("Welcome to LinkPilot", "Step 1 of 3 - how it works")
                Sub("One step is needed - here is why")
                Body("LinkPilot decides which browser each link opens in. For that, every link has to pass through it " +
                     "first, and Android sends links only to your default browser. So LinkPilot has to be your default " +
                     "browser; it then hands each link straight on to the browser you chose. This is the only way it can work.\n" +
                     "Android lets only you make this choice - it asks you itself. Step 3 shows you where.")
                Sub("Why you can trust it")
                Column(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp)).padding(14.dp),
                       verticalArrangement = Arrangement.spacedBy(10.dp)) {
                    Trust("Fully offline", "LinkPilot has no internet permission at all, so Android itself keeps it from " +
                          "going online.")
                    Trust("Sends nothing", "No account, no ads, no tracking, nothing collected. Your links, settings and the " +
                          "link log stay on this phone.")
                    Trust("Made for personal use", "A small project made for private use, and shared freely - nothing is sold.")
                    Trust("Open", "Every file is public on GitHub, to read or to build yourself.")
                    Trust("Easy to undo", "Your browsers stay as they are. Make one of them the default again at any time, or " +
                          "uninstall LinkPilot.")
                }
                Nav(back = "Skip setup" to finish, next = "Next  →" to { step = 2 })
            }
            2 -> {
                Heading("Your choices", "Step 2 of 3 - both can be changed later, on their tabs")
                SwitchRow("Clean links", "Takes tracking out of links - utm_source, fbclid, YouTube's si... - and skips redirects " +
                    "such as google.com/url?q=..., so the page opens directly. Only parts known to be tracking are removed.",
                    store.cleanOn && store.unwrapOn) { store.cleanOn = it; store.unwrapOn = it; m.changed() }
                SwitchRow("Keep a log of links", "Which app each link came from, where it opened, and what was changed - on " +
                    "this phone only, the newest 300.", store.logOn) { store.logOn = it; m.changed() }
                Nav(back = "←  Back" to { step = 1 }, next = "Next  →" to { step = 3 })
            }
            3 -> {
                Heading("Make LinkPilot your default browser", "Step 3 of 3 - the one step Android leaves to you")
                if (isDefault) {
                    Banner("✓", "Already done", "LinkPilot is your default browser - there is nothing to do here.")
                    Nav(back = "←  Back" to { step = 2 }, next = "Next  →" to { step = 4 })
                } else {
                    Body("1.  Press Make it the default below.\n" +
                         "2.  Android asks which app should be your default browser - choose LinkPilot, then Set as default.\n" +
                         "3.  You come back here, and this screen goes on by itself.")
                    Body("If Android does not ask (it stops asking after you said no twice), Open settings takes you to " +
                         "Default apps → Browser app instead.")
                    TextButton(onClick = openSettings) { Text("Open settings") }
                    Nav(back = "←  Back" to { step = 2 }, next = "Make it the default" to { m.askedForDefault = true; makeDefault() })
                }
            }
            else -> {
                Banner("✓", "You're all set!", "LinkPilot is ready.")
                val live = store.live()
                Body((if (live != null) "Links now open in ${live.name} (${m.label(live)}).\n" else "") +
                     "Next: add more categories on Home - Work, Home... each with its browser - and the Quick Settings tiles, " +
                     "to switch between them with one tap.")
                if (Profiles.others(ctx).isNotEmpty())
                    Body("Browsers in your work profile (Island): LinkPilot has to be installed there too - in Island, " +
                         "clone it - so it can hand links over. Add a category on Home then lists them, and says if " +
                         "anything else is still needed.")
                Nav(back = "←  Back" to { step = 3 }, next = "Finish" to finish)
            }
        }
    }
}

@Composable
private fun Heading(title: String, step: String) {
    Column {
        Text(title, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
        Text(step, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

@Composable
private fun Sub(text: String) = Text(text, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)

@Composable
private fun Body(text: String) = Text(text, style = MaterialTheme.typography.bodyMedium)

@Composable
private fun Trust(title: String, text: String) {
    Column {
        Text(title, fontWeight = FontWeight.Bold)
        Text(text, style = MaterialTheme.typography.bodySmall)
    }
}

@Composable
private fun Banner(mark: String, title: String, text: String) {
    Row(Modifier.fillMaxWidth().background(MaterialTheme.colorScheme.secondaryContainer, RoundedCornerShape(12.dp)).padding(16.dp),
        verticalAlignment = Alignment.CenterVertically) {
        Text(mark, fontSize = 32.sp, color = MaterialTheme.colorScheme.onSecondaryContainer)
        Spacer(Modifier.width(14.dp))
        Column {
            Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold,
                 color = MaterialTheme.colorScheme.onSecondaryContainer)
            Text(text, color = MaterialTheme.colorScheme.onSecondaryContainer)
        }
    }
}

@Composable
private fun Nav(back: Pair<String, () -> Unit>, next: Pair<String, () -> Unit>) {
    Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        TextButton(onClick = back.second) { Text(back.first) }
        Spacer(Modifier.weight(1f))
        Button(onClick = next.second) { Text(next.first) }
    }
}
