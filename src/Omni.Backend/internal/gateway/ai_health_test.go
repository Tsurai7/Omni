package gateway

import (
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestCheckOmniAIAtStartup_OK(t *testing.T) {
	t.Setenv("AI_SKIP_HEALTHCHECK", "")
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"status":"ok","service":"omni-ai"}`))
	}))
	defer srv.Close()
	if err := CheckOmniAIAtStartup(srv.URL, nil); err != nil {
		t.Fatal(err)
	}
}

func TestCheckOmniAIAtStartup_WrongServiceBody(t *testing.T) {
	t.Setenv("AI_SKIP_HEALTHCHECK", "")
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = w.Write([]byte(`{"service":"profile"}`))
	}))
	defer srv.Close()
	if err := CheckOmniAIAtStartup(srv.URL, nil); err == nil {
		t.Fatal("expected error for non-omni-ai body")
	}
}

func TestCheckOmniAIAtStartup_Skip(t *testing.T) {
	t.Setenv("AI_SKIP_HEALTHCHECK", "1")
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		http.Error(w, "nope", http.StatusInternalServerError)
	}))
	defer srv.Close()
	if err := CheckOmniAIAtStartup(srv.URL, nil); err != nil {
		t.Fatalf("skip should not probe: %v", err)
	}
}
