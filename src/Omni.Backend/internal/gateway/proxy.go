package gateway

import (
	"log/slog"
	"net/http"
	"net/http/httputil"
	"net/url"
	"time"
)

// ReverseProxyTo creates a handler that proxies requests to the given target base URL.
// The request path and headers (including Authorization) are forwarded as-is.
// FlushInterval is set to 50ms so SSE (Server-Sent Events) responses stream
// immediately instead of being buffered.
func ReverseProxyTo(targetBase string, logger *slog.Logger) (http.Handler, error) {
	target, err := url.Parse(targetBase)
	if err != nil {
		return nil, err
	}
	proxy := httputil.NewSingleHostReverseProxy(target)
	proxy.FlushInterval = 50 * time.Millisecond
	proxy.Director = func(req *http.Request) {
		req.URL.Scheme = target.Scheme
		req.URL.Host = target.Host
		req.Host = target.Host
		logger.Debug("proxying request", "method", req.Method, "path", req.URL.Path, "upstream", targetBase)
	}
	proxy.ErrorHandler = func(w http.ResponseWriter, r *http.Request, err error) {
		logger.Error("upstream proxy error", "method", r.Method, "path", r.URL.Path, "upstream", targetBase, "error", err)
		w.WriteHeader(http.StatusBadGateway)
	}
	return proxy, nil
}
