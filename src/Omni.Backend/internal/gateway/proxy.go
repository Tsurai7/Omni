package gateway

import (
	"net/http"
	"net/http/httputil"
	"net/url"
)

// ReverseProxyTo creates a Gin handler that proxies requests to the given target base URL.
// The request path and headers (including Authorization) are forwarded as-is.
func ReverseProxyTo(targetBase string) (http.Handler, error) {
	target, err := url.Parse(targetBase)
	if err != nil {
		return nil, err
	}
	proxy := httputil.NewSingleHostReverseProxy(target)
	proxy.Director = func(req *http.Request) {
		req.URL.Scheme = target.Scheme
		req.URL.Host = target.Host
		req.Host = target.Host
	}
	return proxy, nil
}
