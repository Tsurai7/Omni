package config

import (
	"net/url"
	"strings"
)

// normalizeGatewayAIURL returns the base URL for reverse-proxying to omni-ai (no trailing slash).
// A trailing path segment /api/ai (case-insensitive, repeated) is stripped: the gateway forwards
// full paths such as /api/ai/chat/... and httputil joins the target base path with the request path,
// so AI_URL must be the service root only (e.g. http://127.0.0.1:8000), not .../api/ai.
func normalizeGatewayAIURL(raw string) (base string, strippedTrailingAPISuffix bool) {
	s := strings.TrimSpace(raw)
	if s == "" {
		return "", false
	}
	s = strings.TrimSuffix(s, "/")
	u, err := url.Parse(s)
	if err != nil || u.Scheme == "" || u.Host == "" {
		return stripTrailingAPISuffixFromPathOnly(s)
	}
	path := strings.TrimSuffix(u.Path, "/")
	for {
		lower := strings.ToLower(path)
		if !strings.HasSuffix(lower, "/api/ai") {
			break
		}
		path = path[:len(path)-len("/api/ai")]
		path = strings.TrimSuffix(path, "/")
		strippedTrailingAPISuffix = true
	}
	u.Path = path
	out := strings.TrimSuffix(u.String(), "/")
	return out, strippedTrailingAPISuffix
}

func stripTrailingAPISuffixFromPathOnly(s string) (string, bool) {
	changed := false
	for {
		lower := strings.ToLower(s)
		if !strings.HasSuffix(lower, "/api/ai") {
			break
		}
		s = s[:len(s)-len("/api/ai")]
		s = strings.TrimSuffix(s, "/")
		changed = true
	}
	return s, changed
}
