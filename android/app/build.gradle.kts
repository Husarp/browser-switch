import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

android {
    namespace = "com.husarp.linkpilot"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.husarp.linkpilot"
        minSdk = 26
        targetSdk = 35
        versionCode = 12
        versionName = "0.8.1"
    }

    // The release key lives outside the project (never published): its file and passwords are in
    // ~/.keystores/linkpilot-signing.properties. Without it - someone else building this - the
    // release build is signed with that PC's debug key instead.
    val signing = Properties().apply {
        val f = File(System.getProperty("user.home"), ".keystores/linkpilot-signing.properties")
        if (f.exists()) f.inputStream().use { load(it) }
    }
    signingConfigs {
        if (signing.isNotEmpty()) create("release") {
            storeFile = file(signing.getProperty("storeFile"))
            storePassword = signing.getProperty("storePassword")
            keyAlias = signing.getProperty("keyAlias")
            keyPassword = signing.getProperty("keyPassword")
        }
    }

    buildTypes {
        // Optimised (R8) - a debug build of Compose scrolls noticeably slower.
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"))
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions { jvmTarget = "17" }

    buildFeatures { compose = true }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.activity:activity-compose:1.9.0")
    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    testImplementation("junit:junit:4.13.2")
}
