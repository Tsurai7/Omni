package httpclient

import (
	"net/http"
	"time"

	"github.com/go-resty/resty/v2"
)

func New() *resty.Client {
	return resty.New().
		SetTimeout(15 * time.Second).
		SetRetryCount(3).
		SetRetryWaitTime(200 * time.Millisecond).
		SetRetryMaxWaitTime(5 * time.Second).
		AddRetryCondition(func(r *resty.Response, err error) bool {
			if err != nil {
				return true
			}
			switch r.StatusCode() {
			case http.StatusTooManyRequests,
				http.StatusInternalServerError,
				http.StatusBadGateway,
				http.StatusServiceUnavailable,
				http.StatusGatewayTimeout:
				return true
			}
			return false
		})
}
