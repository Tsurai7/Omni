package gateway

import (
	"context"
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

func probeAIHealth(ctx context.Context, c *http.Client, u string) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, u, nil)
	if err != nil {
		return err
	}
	resp, err := c.Do(req)
	if err != nil {
		return err
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
	var lastErr error
	for attempt := 0; attempt < 12; attempt++ {
		if attempt > 0 {
			time.Sleep(400 * time.Millisecond)
		}
		if err := probeAIHealth(context.Background(), client, u); err != nil {
			lastErr = err
			continue
		}
		return nil
	}
	return fmt.Errorf("omni-ai health check failed after retries (GET %s): %w — fix AI_URL or start omni-ai; set AI_SKIP_HEALTHCHECK=1 to bypass", u, lastErr)
}

func LogAIReachability(aiURL string, log *slog.Logger) {
	if aiURL == "" {
		return
	}
	go func() {
		time.Sleep(800 * time.Millisecond)
		client := &http.Client{Timeout: 5 * time.Second}
		u := strings.TrimSuffix(aiURL, "/") + "/health"
		if err := probeAIHealth(context.Background(), client, u); err != nil {
			log.Warn("ai health check failed", "url", u, "error", err)
			return
		}
		log.Info("ai service reachable", "url", u)
	}()
}
