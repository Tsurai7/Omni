package main

import "github.com/gin-gonic/gin"

// These handlers are only for Swagger doc generation; routes use proxies.

// authRegister godoc
// @Summary      Register a new user
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  object  true  "Email and password"
// @Success      201   {object}  object
// @Failure      400,409  {object}  map[string]string
// @Router       /auth/register [post]
func authRegister(*gin.Context) {}

// authLogin godoc
// @Summary      Log in
// @Tags         auth
// @Accept       json
// @Produce      json
// @Param        body  body  object  true  "Email and password"
// @Success      200   {object}  object
// @Failure      401   {object}  map[string]string
// @Router       /auth/login [post]
func authLogin(*gin.Context) {}

// authMe godoc
// @Summary      Get current user
// @Tags         auth
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /auth/me [get]
func authMe(*gin.Context) {}

// usageList godoc
// @Summary      List usage entries
// @Tags         usage
// @Security     BearerAuth
// @Produce      json
// @Param        from       query  string  false  "From date (YYYY-MM-DD)"
// @Param        to         query  string  false  "To date (YYYY-MM-DD)"
// @Param        group_by   query  string  false  "day|week|month"
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /usage [get]
func usageList(*gin.Context) {}

// usageSync godoc
// @Summary      Sync usage entries
// @Tags         usage
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  object  true  "Usage entries"
// @Success      200   {object}  object
// @Failure      400,401  {object}  map[string]string
// @Router       /usage/sync [post]
func usageSync(*gin.Context) {}

// sessionsList godoc
// @Summary      List sessions
// @Tags         sessions
// @Security     BearerAuth
// @Produce      json
// @Param        from  query  string  false  "From date (YYYY-MM-DD)"
// @Param        to    query  string  false  "To date (YYYY-MM-DD)"
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /sessions [get]
func sessionsList(*gin.Context) {}

// sessionsSync godoc
// @Summary      Sync session entries
// @Tags         sessions
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Param        body  body  object  true  "Session entries"
// @Success      200   {object}  object
// @Failure      400,401  {object}  map[string]string
// @Router       /sessions/sync [post]
func sessionsSync(*gin.Context) {}

// tasksList godoc
// @Summary      List tasks
// @Tags         tasks
// @Security     BearerAuth
// @Produce      json
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /tasks [get]
func tasksList(*gin.Context) {}

// tasksCreate godoc
// @Summary      Create a task
// @Tags         tasks
// @Security     BearerAuth
// @Accept       json
// @Produce      json
// @Success      201  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /tasks [post]
func tasksCreate(*gin.Context) {}

// tasksUpdateStatus godoc
// @Summary      Update task status
// @Tags         tasks
// @Security     BearerAuth
// @Produce      json
// @Param        id   path  string  true  "Task ID"
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /tasks/{id}/status [patch]
func tasksUpdateStatus(*gin.Context) {}

// tasksDelete godoc
// @Summary      Delete a task
// @Tags         tasks
// @Security     BearerAuth
// @Produce      json
// @Param        id   path  string  true  "Task ID"
// @Success      200  {object}  object
// @Failure      401  {object}  map[string]string
// @Router       /tasks/{id} [delete]
func tasksDelete(*gin.Context) {}
