package calendar

import (
	"context"
	"fmt"
	"net/url"
	"time"

	"omni-backend/internal/httpclient"

	"github.com/go-resty/resty/v2"
)

const (
	googleTokenURL    = "https://oauth2.googleapis.com/token"
	googleCalendarURL = "https://www.googleapis.com/calendar/v3"
	googleAuthURL     = "https://accounts.google.com/o/oauth2/v2/auth"
	calendarScope     = "https://www.googleapis.com/auth/calendar.events"
	userInfoScope     = "https://www.googleapis.com/auth/userinfo.email"
)

type GoogleClient struct {
	ClientID     string
	ClientSecret string
	RedirectURI  string
	client       *resty.Client
}

func NewGoogleClient(clientID, clientSecret, redirectURI string) *GoogleClient {
	return &GoogleClient{
		ClientID:     clientID,
		ClientSecret: clientSecret,
		RedirectURI:  redirectURI,
		client:       httpclient.New(),
	}
}

func (g *GoogleClient) AuthURL(state string) string {
	params := url.Values{}
	params.Set("client_id", g.ClientID)
	params.Set("redirect_uri", g.RedirectURI)
	params.Set("response_type", "code")
	params.Set("scope", calendarScope+" "+userInfoScope)
	params.Set("access_type", "offline")
	params.Set("prompt", "consent")
	if state != "" {
		params.Set("state", state)
	}
	return googleAuthURL + "?" + params.Encode()
}

type tokenResponse struct {
	AccessToken  string `json:"access_token"`
	RefreshToken string `json:"refresh_token"`
	ExpiresIn    int    `json:"expires_in"`
	TokenType    string `json:"token_type"`
	Error        string `json:"error"`
}

func (g *GoogleClient) ExchangeCode(ctx context.Context, code string) (*tokenResponse, error) {
	var tr tokenResponse
	resp, err := g.client.R().
		SetContext(ctx).
		SetFormData(map[string]string{
			"code":          code,
			"client_id":     g.ClientID,
			"client_secret": g.ClientSecret,
			"redirect_uri":  g.RedirectURI,
			"grant_type":    "authorization_code",
		}).
		SetResult(&tr).
		Post(googleTokenURL)
	if err != nil {
		return nil, err
	}
	if resp.IsError() {
		return nil, fmt.Errorf("google token error %d: %s", resp.StatusCode(), resp.String())
	}
	if tr.Error != "" {
		return nil, fmt.Errorf("google token error: %s", tr.Error)
	}
	return &tr, nil
}

func (g *GoogleClient) RefreshAccessToken(ctx context.Context, refreshToken string) (*tokenResponse, error) {
	var tr tokenResponse
	resp, err := g.client.R().
		SetContext(ctx).
		SetFormData(map[string]string{
			"refresh_token": refreshToken,
			"client_id":     g.ClientID,
			"client_secret": g.ClientSecret,
			"grant_type":    "refresh_token",
		}).
		SetResult(&tr).
		Post(googleTokenURL)
	if err != nil {
		return nil, err
	}
	if resp.IsError() {
		return nil, fmt.Errorf("google refresh error %d: %s", resp.StatusCode(), resp.String())
	}
	if tr.Error != "" {
		return nil, fmt.Errorf("google refresh error: %s", tr.Error)
	}
	return &tr, nil
}

func (g *GoogleClient) GetUserEmail(ctx context.Context, accessToken string) (string, error) {
	var info struct {
		Email string `json:"email"`
	}
	resp, err := g.client.R().
		SetContext(ctx).
		SetAuthToken(accessToken).
		SetResult(&info).
		Get("https://www.googleapis.com/oauth2/v2/userinfo")
	if err != nil {
		return "", err
	}
	if resp.IsError() {
		return "", fmt.Errorf("google userinfo error %d: %s", resp.StatusCode(), resp.String())
	}
	return info.Email, nil
}

type GoogleCalendarEvent struct {
	ID          string `json:"id"`
	Summary     string `json:"summary"`
	Description string `json:"description"`
	ColorID     string `json:"colorId"`
	Start       struct {
		DateTime string `json:"dateTime"`
		Date     string `json:"date"`
		TimeZone string `json:"timeZone"`
	} `json:"start"`
	End struct {
		DateTime string `json:"dateTime"`
		Date     string `json:"date"`
		TimeZone string `json:"timeZone"`
	} `json:"end"`
	Status  string `json:"status"`
	Updated string `json:"updated"`
}

type eventsListResponse struct {
	Items         []GoogleCalendarEvent `json:"items"`
	NextPageToken string                `json:"nextPageToken"`
}

func (g *GoogleClient) ListEvents(ctx context.Context, accessToken string, timeMin, timeMax time.Time) ([]GoogleCalendarEvent, error) {
	var result eventsListResponse
	resp, err := g.client.R().
		SetContext(ctx).
		SetAuthToken(accessToken).
		SetQueryParams(map[string]string{
			"timeMin":      timeMin.UTC().Format(time.RFC3339),
			"timeMax":      timeMax.UTC().Format(time.RFC3339),
			"singleEvents": "true",
			"orderBy":      "startTime",
			"maxResults":   "250",
		}).
		SetResult(&result).
		Get(googleCalendarURL + "/calendars/primary/events")
	if err != nil {
		return nil, err
	}
	if resp.IsError() {
		return nil, fmt.Errorf("google calendar API error %d: %s", resp.StatusCode(), resp.String())
	}
	return result.Items, nil
}

type createEventBody struct {
	Summary     string              `json:"summary"`
	Description string              `json:"description,omitempty"`
	Start       googleEventDateTime `json:"start"`
	End         googleEventDateTime `json:"end"`
}

type googleEventDateTime struct {
	DateTime string `json:"dateTime,omitempty"`
	Date     string `json:"date,omitempty"`
	TimeZone string `json:"timeZone,omitempty"`
}

func buildEventBody(title, description string, start time.Time, endTime *time.Time, isAllDay bool) createEventBody {
	body := createEventBody{Summary: title, Description: description}
	if isAllDay {
		body.Start.Date = start.Format("2006-01-02")
		body.End.Date = start.AddDate(0, 0, 1).Format("2006-01-02")
	} else {
		end := start.Add(time.Hour)
		if endTime != nil {
			end = *endTime
		}
		body.Start.DateTime = start.UTC().Format(time.RFC3339)
		body.Start.TimeZone = "UTC"
		body.End.DateTime = end.UTC().Format(time.RFC3339)
		body.End.TimeZone = "UTC"
	}
	return body
}

func (g *GoogleClient) CreateEvent(ctx context.Context, accessToken, title, description string, start time.Time, endTime *time.Time, isAllDay bool) (*GoogleCalendarEvent, error) {
	var evt GoogleCalendarEvent
	resp, err := g.client.R().
		SetContext(ctx).
		SetAuthToken(accessToken).
		SetBody(buildEventBody(title, description, start, endTime, isAllDay)).
		SetResult(&evt).
		Post(googleCalendarURL + "/calendars/primary/events")
	if err != nil {
		return nil, err
	}
	if resp.IsError() {
		return nil, fmt.Errorf("google calendar API error %d: %s", resp.StatusCode(), resp.String())
	}
	return &evt, nil
}

func (g *GoogleClient) UpdateEvent(ctx context.Context, accessToken, eventID, title, description string, start time.Time, isAllDay bool) error {
	resp, err := g.client.R().
		SetContext(ctx).
		SetAuthToken(accessToken).
		SetBody(buildEventBody(title, description, start, nil, isAllDay)).
		Put(googleCalendarURL + "/calendars/primary/events/" + eventID)
	if err != nil {
		return err
	}
	if resp.IsError() {
		return fmt.Errorf("google calendar API error %d: %s", resp.StatusCode(), resp.String())
	}
	return nil
}

func (g *GoogleClient) DeleteEvent(ctx context.Context, accessToken, eventID string) error {
	resp, err := g.client.R().
		SetContext(ctx).
		SetAuthToken(accessToken).
		Delete(googleCalendarURL + "/calendars/primary/events/" + eventID)
	if err != nil {
		return err
	}
	if resp.IsError() {
		return fmt.Errorf("google calendar API error %d: %s", resp.StatusCode(), resp.String())
	}
	return nil
}
