package md.leia.assistant;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public final class SecureKeyStore {
    private static final String KS = "AndroidKeyStore";
    private static final String ALIAS = "leia_openai_key";
    private static final String PREFS = "leia_secrets";
    private static final String ENC = "openai_key_enc";
    private static final String IV = "openai_key_iv";

    private SecureKeyStore() {}

    private static SecretKey getOrCreate() throws Exception {
        KeyStore ks = KeyStore.getInstance(KS);
        ks.load(null);
        if (ks.containsAlias(ALIAS)) {
            return ((KeyStore.SecretKeyEntry) ks.getEntry(ALIAS, null)).getSecretKey();
        }
        KeyGenerator kg = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KS);
        kg.init(new KeyGenParameterSpec.Builder(
                ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .build());
        return kg.generateKey();
    }

    public static void save(Context c, String apiKey) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, getOrCreate());
        byte[] enc = cipher.doFinal(apiKey.getBytes(StandardCharsets.UTF_8));
        SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        p.edit()
                .putString(ENC, Base64.encodeToString(enc, Base64.NO_WRAP))
                .putString(IV, Base64.encodeToString(cipher.getIV(), Base64.NO_WRAP))
                .apply();
    }

    public static String load(Context c) {
        try {
            SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
            String encS = p.getString(ENC, null);
            String ivS = p.getString(IV, null);
            if (encS == null || ivS == null) return null;

            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, getOrCreate(),
                    new GCMParameterSpec(128, Base64.decode(ivS, Base64.NO_WRAP)));
            byte[] dec = cipher.doFinal(Base64.decode(encS, Base64.NO_WRAP));
            return new String(dec, StandardCharsets.UTF_8);
        } catch (Throwable t) {
            return null;
        }
    }

    public static boolean has(Context c) {
        String k = load(c);
        return k != null && k.startsWith("sk-");
    }

    public static void clear(Context c) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().clear().apply();
    }
}
