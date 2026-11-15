package com.takemetothemoon.visko;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.URL;
import java.util.Iterator;
import javax.net.ssl.HttpsURLConnection;
import org.json.JSONObject;

/** Android's TLS stack supports TLS 1.3 on Quest 3. Uses default certificate/hostname validation. */
public final class ReactorHttps {
    public static String request(String method, String url, String headersJson, byte[] body) {
        HttpsURLConnection connection = null;
        JSONObject result = new JSONObject();
        try {
            URL target = new URL(url);
            if (!"https".equals(target.getProtocol())) throw new IllegalArgumentException("HTTPS required");
            connection = (HttpsURLConnection)target.openConnection();
            connection.setInstanceFollowRedirects(false);
            connection.setConnectTimeout(15000);
            connection.setReadTimeout(30000);
            connection.setRequestMethod(method);
            JSONObject headers = new JSONObject(headersJson);
            Iterator<String> names = headers.keys();
            while (names.hasNext()) {
                String name = names.next();
                connection.setRequestProperty(name, headers.getString(name));
            }
            if (body != null && body.length > 0) {
                connection.setDoOutput(true);
                connection.setFixedLengthStreamingMode(body.length);
                try (java.io.OutputStream output = connection.getOutputStream()) { output.write(body); }
            }
            int status = connection.getResponseCode();
            result.put("status", status);
            InputStream stream = status < 400 ? connection.getInputStream() : connection.getErrorStream();
            if (stream != null) {
                try (InputStream input = stream; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
                    byte[] buffer = new byte[8192]; int size;
                    while ((size = input.read(buffer)) != -1) output.write(buffer, 0, size);
                    result.put("body", output.toString("UTF-8"));
                }
            }
        } catch (Exception e) {
            try { result.put("error", e.getClass().getSimpleName()); } catch (Exception ignored) { }
        } finally { if (connection != null) connection.disconnect(); }
        return result.toString();
    }
}
