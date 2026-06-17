// Push a day's waste-collection rounds to the Ingest service (minimal Java example).
//
// Single-file program: run it directly with `java PushWasteRounds.java` (Java 11+
// source-file mode) - no build tool, no dependencies. It reads a round-level CSV
// export of the kind a waste-management / in-cab system produces, aggregates it
// into the daily KPIs of the `garbage_collection` schema, and POSTs one submission.
//
// The request JSON is built by hand to avoid pulling in a JSON library; the raw
// response body is printed as-is (it contains the new submission id and any
// warnings/errors).
//
// Usage:
//   set INGEST_BASE_URL=https://ingest.example.org
//   set INGEST_API_KEY=abc12345.your-secret-here
//   java PushWasteRounds.java [--csv rounds_export_2026-06-15.csv] [--dry-run]

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

public class PushWasteRounds {

    static final String SCHEMA_NAME = "garbage_collection";

    public static void main(String[] args) throws Exception {
        boolean dryRun = Arrays.asList(args).contains("--dry-run");

        String csvPath = "rounds_export_2026-06-15.csv";
        int csvFlag = Arrays.asList(args).indexOf("--csv");
        if (csvFlag >= 0 && csvFlag + 1 < args.length) {
            csvPath = args[csvFlag + 1];
        }

        List<String> lines = Files.readAllLines(Path.of(csvPath), StandardCharsets.UTF_8);
        if (lines.size() <= 1) {
            System.err.println("No rounds found in the export - nothing to submit.");
            System.exit(1);
        }

        int completed = 0, missed = 0, breakdowns = 0;
        double generalTonnes = 0, recyclingTonnes = 0, weightedContamination = 0;
        String collectionDate = null;
        List<String> missReasons = new ArrayList<>();
        List<String> breakdownDescriptions = new ArrayList<>();

        for (int i = 1; i < lines.size(); i++) {
            String line = lines.get(i);
            if (line.isBlank()) continue;
            // Minimal parser: assumes no embedded commas in the sample export.
            String[] c = line.split(",", -1);

            if (collectionDate == null) collectionDate = c[3];
            String status = c[4].trim().toLowerCase();
            String roundName = c[1];
            double general = parseDouble(c[6]);
            double recycling = parseDouble(c[7]);
            double contamination = parseDouble(c[8]);

            if (status.equals("completed")) completed++;
            if (status.equals("missed")) {
                missed++;
                if (!c[5].trim().isEmpty()) missReasons.add(roundName + ": " + c[5].trim());
            }

            generalTonnes += general;
            recyclingTonnes += recycling;
            if (recycling > 0) weightedContamination += recycling * contamination;

            if (c[10].trim().equalsIgnoreCase("Y")) {
                breakdowns++;
                if (!c[11].trim().isEmpty()) {
                    breakdownDescriptions.add(c[9] + " (" + roundName + "): " + c[11].trim());
                }
            }
        }

        // Daily cadence: one sample per day bucket, end-of-shift UTC timestamp.
        String timestamp = collectionDate + "T17:00:00Z";
        double contaminationPct = recyclingTonnes > 0 ? round2(weightedContamination / recyclingTonnes) : 0;

        List<String> samples = new ArrayList<>();
        // tonnes_collected is the gate total (recycling included) so recycling <= total holds.
        samples.add(sample("tonnes_collected", num(round2(generalTonnes + recyclingTonnes)), timestamp));
        samples.add(sample("routes_completed", Integer.toString(completed), timestamp));
        samples.add(sample("routes_missed", Integer.toString(missed), timestamp));
        samples.add(sample("vehicle_breakdowns", Integer.toString(breakdowns), timestamp));
        samples.add(sample("recycling_tonnes_collected", num(round2(recyclingTonnes)), timestamp));

        // Conditional fields mirror the schema's visibleIf rules: only send when relevant.
        if (missed > 0) {
            samples.add(sample("routes_missed_reason", str(String.join("; ", missReasons)), timestamp));
        }
        if (breakdowns > 0) {
            samples.add(sample("breakdown_description", str(String.join("; ", breakdownDescriptions)), timestamp));
        }
        if (recyclingTonnes > 0) {
            samples.add(sample("contamination_pct", num(contaminationPct), timestamp));
        }

        String body = "{\"samples\":[" + String.join(",", samples) + "]}";

        if (dryRun) {
            System.out.println(body);
            return;
        }

        String baseUrl = System.getenv("INGEST_BASE_URL");
        String apiKey = System.getenv("INGEST_API_KEY");
        if (baseUrl == null || baseUrl.isBlank() || apiKey == null || apiKey.isBlank()) {
            System.err.println("Set INGEST_BASE_URL and INGEST_API_KEY (or use --dry-run).");
            System.exit(1);
        }

        HttpClient http = HttpClient.newHttpClient();
        HttpRequest request = HttpRequest.newBuilder()
                .uri(URI.create(stripTrailingSlash(baseUrl) + "/api/submissions"))
                .header("Content-Type", "application/json")
                .header("X-Api-Key", apiKey)
                .POST(HttpRequest.BodyPublishers.ofString(body, StandardCharsets.UTF_8))
                .build();

        HttpResponse<String> response = http.send(request, HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() >= 200 && response.statusCode() < 300) {
            System.out.println("Created submission (HTTP " + response.statusCode() + "): " + response.body());
        } else {
            System.err.println("Submission failed: HTTP " + response.statusCode());
            System.err.println("  " + response.body());
            System.exit(1);
        }
    }

    static String sample(String valueName, String valueJson, String timestamp) {
        return "{\"schemaName\":" + str(SCHEMA_NAME)
                + ",\"valueName\":" + str(valueName)
                + ",\"value\":" + valueJson
                + ",\"timestamp\":" + str(timestamp)
                + ",\"note\":null}";
    }

    static String num(double n) {
        return Double.toString(n);
    }

    static double round2(double n) {
        return Math.round(n * 100.0) / 100.0;
    }

    static double parseDouble(String s) {
        try {
            return Double.parseDouble(s.trim());
        } catch (NumberFormatException e) {
            return 0;
        }
    }

    static String stripTrailingSlash(String s) {
        return s.endsWith("/") ? s.substring(0, s.length() - 1) : s;
    }

    static String str(String s) {
        StringBuilder sb = new StringBuilder("\"");
        for (int i = 0; i < s.length(); i++) {
            char ch = s.charAt(i);
            switch (ch) {
                case '\\': sb.append("\\\\"); break;
                case '"': sb.append("\\\""); break;
                case '\n': sb.append("\\n"); break;
                case '\r': sb.append("\\r"); break;
                case '\t': sb.append("\\t"); break;
                default: sb.append(ch);
            }
        }
        return sb.append('"').toString();
    }
}
