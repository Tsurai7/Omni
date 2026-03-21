package calendar

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"
)

const (
	googleTokenURL    = "https://oauth2.googleapis.com/token"
	googleCalendarURL = "https://www.googleapis.com/calendar/v3"
	googleAuthURL     = "https://accounts.google.com/o/oauth2/v2/auth"
	calendarScope     = "https://www.googleapis.com/auth/calendar.events"
	userInfoScope     = "https://www.googleapis.com/auth/userinfo.email"
)

// GoogleClient wraps Google OAuth2 and Calendar API calls.
type GoogleClient struct {
	ClientID     string
	ClientSecret string
	RedirectURI  string
	httpClient   *http.Client
}

func NewGoogleClient(clientID, clientSecret, redirectURI string) *GoogleClient {
	return &GoogleClient{
		ClientID:     clientID,
		ClientSecret: clientSecret,
		RedirectURI:  redirectURI,
		httpClient:   &http.Client{Timeout: 15 * time.Second},
	}
}

// AuthURL returns the Google OAuth2 consent URL.
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

// ExchangeCode trades an authorization code for access+refresh tokens.
func (g *GoogleClient) ExchangeCode(ctx context.Context, code string) (*tokenResponse, error) {
	data := url.Values{}
	data.Set("code", code)
	data.Set("client_id", g.ClientID)
	data.Set("client_secret", g.ClientSecret)
	data.Set("redirect_uri", g.RedirectURI)
	data.Set("grant_type", "authorization_code")

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, googleTokenURL, strings.NewReader(data.Encode()))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var tr tokenResponse
	if err := json.Unmarshal(body, &tr); err != nil {
		return nil, err
	}
	if tr.Error != "" {
		return nil, fmt.Errorf("google token error: %s", tr.Error)
	}
	return &tr, nil
}

// RefreshAccessToken uses a refresh token to get a new access token.
func (g *GoogleClient) RefreshAccessToken(ctx context.Context, refreshToken string) (*tokenResponse, error) {
	data := url.Values{}
	data.Set("refresh_token", refreshToken)
	data.Set("client_id", g.ClientID)
	data.Set("client_secret", g.ClientSecret)
	data.Set("grant_type", "refresh_token")

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, googleTokenURL, strings.NewReader(data.Encode()))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var tr tokenResponse
	if err := json.Unmarshal(body, &tr); err != nil {
		return nil, err
	}
	if tr.Error != "" {
		return nil, fmt.Errorf("google refresh error: %s", tr.Error)
	}
	return &tr, nil
}

// GetUserEmail fetches the Google account email using the access token.
func (g *GoogleClient) GetUserEmail(ctx context.Context, accessToken string) (string, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet,
		"https://www.googleapis.com/oauth2/v2/userinfo", nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return "", err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", err
	}

	var info struct {
		Email string `json:"email"`
	}
	if err := json.Unmarshal(body, &info); err != nil {
		return "", err
	}
	return info.Email, nil
}

// GoogleCalendarEvent is a raw event from the Google Calendar API.
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

// ListEvents fetches events from the user's primary Google Calendar.
func (g *GoogleClient) ListEvents(ctx context.Context, accessToken string, timeMin, timeMax time.Time) ([]GoogleCalendarEvent, error) {
	params := url.Values{}
	params.Set("timeMin", timeMin.UTC().Format(time.RFC3339))
	params.Set("timeMax", timeMax.UTC().Format(time.RFC3339))
	params.Set("singleEvents", "true")
	params.Set("orderBy", "startTime")
	params.Set("maxResults", "250")

	reqURL := googleCalendarURL + "/calendars/primary/events?" + params.Encode()
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, reqURL, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("google calendar API error %d: %s", resp.StatusCode, string(body))
	}

	var result eventsListResponse
	if err := json.Unmarshal(body, &result); err != nil {
		return nil, err
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

// CreateEvent creates a new event in the user's primary Google Calendar.
// If endTime is nil and isAllDay is false, the event defaults to 1 hour.
func (g *GoogleClient) CreateEvent(ctx context.Context, accessToken, title, description string, start time.Time, endTime *time.Time, isAllDay bool) (*GoogleCalendarEvent, error) {
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

	raw, err := json.Marshal(body)
	if err != nil {
		return nil, err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost,
		googleCalendarURL+"/calendars/primary/events",
		strings.NewReader(string(raw)))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)
	req.Header.Set("Content-Type", "application/json")

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	respBody, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return nil, fmt.Errorf("google calendar API error %d: %s", resp.StatusCode, string(respBody))
	}

	var evt GoogleCalendarEvent
	if err := json.Unmarshal(respBody, &evt); err != nil {
		return nil, err
	}
	return &evt, nil
}

// UpdateEvent updates an existing Google Calendar event.
func (g *GoogleClient) UpdateEvent(ctx context.Context, accessToken, eventID, title, description string, start time.Time, isAllDay bool) error {
	body := createEventBody{Summary: title, Description: description}
	if isAllDay {
		body.Start.Date = start.Format("2006-01-02")
		body.End.Date = start.AddDate(0, 0, 1).Format("2006-01-02")
	} else {
		body.Start.DateTime = start.UTC().Format(time.RFC3339)
		body.Start.TimeZone = "UTC"
		body.End.DateTime = start.Add(time.Hour).UTC().Format(time.RFC3339)
		body.End.TimeZone = "UTC"
	}

	raw, err := json.Marshal(body)
	if err != nil {
		return err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPut,
		googleCalendarURL+"/calendars/primary/events/"+eventID,
		strings.NewReader(string(raw)))
	if err != nil {
		return err
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)
	req.Header.Set("Content-Type", "application/json")

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		body, _ := io.ReadAll(resp.Body)
		return fmt.Errorf("google calendar API error %d: %s", resp.StatusCode, string(body))
	}
	return nil
}

// DeleteEvent removes a Google Calendar event.
func (g *GoogleClient) DeleteEvent(ctx context.Context, accessToken, eventID string) error {
	req, err := http.NewRequestWithContext(ctx, http.MethodDelete,
		googleCalendarURL+"/calendars/primary/events/"+eventID, nil)
	if err != nil {
		return err
	}
	req.Header.Set("Authorization", "Bearer "+accessToken)

	resp, err := g.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		body, _ := io.ReadAll(resp.Body)
		return fmt.Errorf("google calendar API error %d: %s", resp.StatusCode, string(body))
	}
	return nil
}
