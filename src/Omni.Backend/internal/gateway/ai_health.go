package gateway

import (
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"os"
	"strings"
	"time"
)

func shouldSkipAIStartupHealthcheck() bool {
	s := strings.TrimSpace(os.Getenv("AI_SKIP_HEALTHCHECK"))
	switch strings.ToLower(s) {
	case "1", "true", "yes", "on":
		return true
	default:
		return false
	}
}

// CheckOmniAIAtStartup fails unless GET {aiURL}/health returns 200 with an omni-ai service id in the body.
// Use when AI_URL is set so misconfiguration fails fast instead of surfacing as 404 on /api/ai/*.
// Set AI_SKIP_HEALTHCHECK=1 (or true/yes/on) to bypass. No-op when aiURL is empty.
func CheckOmniAIAtStartup(aiURL string, log *slog.Logger) error {
	if aiURL == "" {
		return nil
	}
	if shouldSkipAIStartupHealthcheck() {
		if log != nil {
			log.Info("skipping omni-ai startup health check (AI_SKIP_HEALTHCHECK)")
		}
		return nil
	}
	client := &http.Client{Timeout: 5 * time.Second}
	u := strings.TrimSuffix(aiURL, "/") + "/health"
	var lastNetErr error
	for attempt := 0; attempt < 12; attempt++ {
		if attempt > 0 {
			time.Sleep(400 * time.Millisecond)
		}
		resp, err := client.Get(u)
		if err != nil {
			lastNetErr = err
			continue
		}
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 512))
		resp.Body.Close()
		if resp.StatusCode != http.StatusOK {
			return fmt.Errorf("omni-ai health check GET %s returned %s body=%q — AI_URL must point at omni-ai service root", u, resp.Status, string(body))
		}
		if !strings.Contains(string(body), "omni-ai") {
			return fmt.Errorf("omni-ai health check GET %s: body missing service id (expected omni-ai), got %q — wrong AI_URL target?", u, string(body))
		}
		return nil
	}
	return fmt.Errorf("omni-ai health check failed after retries (GET %s): %w — fix AI_URL or start omni-ai; set AI_SKIP_HEALTHCHECK=1 to bypass", u, lastNetErr)
}

// LogAIReachability probes GET {aiURL}/health after a short delay and logs whether
// the omni-ai service is reachable. Misconfigured AI_URL (e.g. pointing at profile/task)
// often surfaces as 404 on /api/ai/*; this helps catch wrong targets early.
func LogAIReachability(aiURL string, log *slog.Logger) {
	if aiURL == "" {
		return
	}
	go func() {
		time.Sleep(800 * time.Millisecond)
		client := &http.Client{Timeout: 5 * time.Second}
		u := strings.TrimSuffix(aiURL, "/") + "/health"
		resp, err := client.Get(u)
		if err != nil {
			log.Warn("ai health check failed (still starting or wrong host/port?)", "url", u, "error", err)
			return
		}
		defer resp.Body.Close()
		body, _ := io.ReadAll(io.LimitReader(resp.Body, 512))
		if resp.StatusCode != http.StatusOK {
			log.Warn("ai health check non-OK", "url", u, "status", resp.StatusCode, "body", string(body))
			return
		}
		if !strings.Contains(string(body), "omni-ai") {
			log.Warn("AI_URL does not look like omni-ai — /health body missing service id", "url", u, "body", string(body))
		} else {
			log.Info("ai service reachable", "url", u)
		}
	}()
}
