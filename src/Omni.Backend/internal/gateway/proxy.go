package gateway

import (
	"log/slog"
	"net/http"
	"net/http/httputil"
	"net/url"
	"time"
)

func ReverseProxyTo(targetBase string, logger *slog.Logger) (http.Handler, error) {
	target, err := url.Parse(targetBase)
	if err != nil {
		return nil, err
	}
	proxy := &httputil.ReverseProxy{
		Rewrite: func(r *httputil.ProxyRequest) {
			r.SetURL(target)
			logger.Debug("proxying request", "method", r.In.Method, "path", r.In.URL.Path, "upstream", targetBase)
		},
		FlushInterval: 50 * time.Millisecond,
		ModifyResponse: func(resp *http.Response) error {
			if resp.StatusCode == http.StatusNotFound && resp.Request != nil {
				logger.Warn("upstream returned 404 — wrong service? AI_URL must be the omni-ai base (e.g. http://ai:8000 in Docker, http://127.0.0.1:8000 on host)",
					"method", resp.Request.Method,
					"path", resp.Request.URL.Path,
					"upstream", targetBase)
			}
			return nil
		},
		ErrorHandler: func(w http.ResponseWriter, r *http.Request, err error) {
			logger.Error("upstream proxy error", "method", r.Method, "path", r.URL.Path, "upstream", targetBase, "error", err)
			w.WriteHeader(http.StatusBadGateway)
		},
	}
	return proxy, nil
}
