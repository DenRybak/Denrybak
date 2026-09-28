package md.leia.assistant;

import android.content.Context;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public final class GptClient {
    public interface Callback {
        void onResult(String text);
        void onError(String error);
    }

    private GptClient() {}

    public static void ask(Context context, String userText, Callback cb) {
        new Thread(() -> {
            String apiKey = SecureKeyStore.load(context);
            if (apiKey == null || apiKey.isBlank()) {
                cb.onError("GPT ещё не подключён. Открой приложение «Лея Assistant» и подключи OpenAI.");
                return;
            }

            HttpURLConnection con = null;
            try {
                URL url = new URL("https://api.openai.com/v1/responses");
                con = (HttpURLConnection) url.openConnection();
                con.setConnectTimeout(15000);
                con.setReadTimeout(60000);
                con.setRequestMethod("POST");
                con.setRequestProperty("Authorization", "Bearer " + apiKey);
                con.setRequestProperty("Content-Type", "application/json");
                con.setDoOutput(true);

                JSONObject body = new JSONObject();
                body.put("model", "gpt-5.6-luna");
                body.put("instructions",
                        "Ты Лея — голосовой системный ассистент Android. " +
                        "Отвечай по-русски, кратко и естественно для голосового ответа. " +
                        "Не говори, что ты отдельное приложение ChatGPT.");
                body.put("input", userText);

                try (OutputStream os = con.getOutputStream()) {
                    os.write(body.toString().getBytes(StandardCharsets.UTF_8));
                }

                int code = con.getResponseCode();
                BufferedReader br = new BufferedReader(new InputStreamReader(
                        code >= 200 && code < 300 ? con.getInputStream() : con.getErrorStream(),
                        StandardCharsets.UTF_8));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = br.readLine()) != null) sb.append(line);

                if (code < 200 || code >= 300) {
                    cb.onError("OpenAI API: " + code);
                    return;
                }

                JSONObject json = new JSONObject(sb.toString());
                String text = extractText(json);
                cb.onResult(text == null || text.isBlank() ? "Не удалось получить ответ." : text);
            } catch (Throwable t) {
                cb.onError("Ошибка связи с GPT: " + t.getClass().getSimpleName());
            } finally {
                if (con != null) con.disconnect();
            }
        }, "leia-gpt").start();
    }

    private static String extractText(JSONObject root) {
        String direct = root.optString("output_text", "");
        if (!direct.isBlank()) return direct;

        JSONArray out = root.optJSONArray("output");
        if (out == null) return null;
        StringBuilder result = new StringBuilder();
        for (int i = 0; i < out.length(); i++) {
            JSONObject item = out.optJSONObject(i);
            if (item == null || !"message".equals(item.optString("type"))) continue;
            JSONArray content = item.optJSONArray("content");
            if (content == null) continue;
            for (int j = 0; j < content.length(); j++) {
                JSONObject c = content.optJSONObject(j);
                if (c != null && "output_text".equals(c.optString("type"))) {
                    if (result.length() > 0) result.append('\n');
                    result.append(c.optString("text"));
                }
            }
        }
        return result.toString();
    }
}
