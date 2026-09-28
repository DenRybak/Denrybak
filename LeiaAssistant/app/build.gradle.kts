plugins {
    id("com.android.application")
}

android {
    namespace = "md.leia.assistant"
    compileSdk = 35

    defaultConfig {
        applicationId = "md.leia.assistant"
        minSdk = 29
        targetSdk = 35
        versionCode = 100
        versionName = "1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}
