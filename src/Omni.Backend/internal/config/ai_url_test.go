package config

import "testing"

func TestNormalizeGatewayAIURL(t *testing.T) {
	tests := []struct {
		raw          string
		wantBase     string
		wantStripped bool
	}{
		{"", "", false},
		{"  ", "", false},
		{"http://127.0.0.1:8000", "http://127.0.0.1:8000", false},
		{"http://127.0.0.1:8000/", "http://127.0.0.1:8000", false},
		{"http://127.0.0.1:8000/api/ai", "http://127.0.0.1:8000", true},
		{"http://127.0.0.1:8000/API/AI/", "http://127.0.0.1:8000", true},
		{"http://127.0.0.1:8000/api/ai/api/ai", "http://127.0.0.1:8000", true},
		{"http://ai:8000/api/ai", "http://ai:8000", true},
	}
	for _, tt := range tests {
		got, stripped := normalizeGatewayAIURL(tt.raw)
		if got != tt.wantBase || stripped != tt.wantStripped {
			t.Errorf("normalizeGatewayAIURL(%q) = (%q, %v); want (%q, %v)",
				tt.raw, got, stripped, tt.wantBase, tt.wantStripped)
		}
	}
}
