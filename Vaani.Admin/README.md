# Vaani Admin Console

A modern single-page web application built with React and Vite for managing Vaani meetings. This admin console allows administrators to authenticate, view, create, update, and delete meetings with real-time status tracking.

## Features

- ?? **Admin Authentication** - Secure login with JWT token-based authentication
- ?? **Meeting Dashboard** - View all meetings with filtering by status (All, Upcoming, Completed)
- ? **Add Meetings** - Create new meetings with validation
- ?? **Edit Meetings** - Update upcoming meetings (completed meetings are read-only)
- ??? **Delete Meetings** - Remove upcoming meetings
- ?? **Status Tracking** - Automatic status calculation based on end date
- ?? **Responsive Design** - Works seamlessly on desktop and mobile devices

## Prerequisites

- Node.js (version 18 or higher)
- npm or yarn package manager
- Vaani.API backend running on `https://localhost:7020`

## Installation

1. Navigate to the project directory:
   ```bash
   cd Vaani.Admin
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Create environment file (optional):
   ```bash
   cp .env.example .env
   ```

## Configuration

By default, the application proxies API requests to `https://localhost:7020`. You can customize this by setting the `VITE_API_URL` environment variable in your `.env` file:

```env
VITE_API_URL=https://your-api-url.com/api
```

## Development

Start the development server:

```bash
npm run dev
```

The application will open at `http://localhost:3000`

## Build

Create a production build:

```bash
npm run build
```

The build output will be in the `dist` folder.

## Preview Production Build

Preview the production build locally:

```bash
npm run preview
```

## Project Structure

```
Vaani.Admin/
??? src/
?   ??? components/          # Reusable UI components
?   ?   ??? Layout.jsx       # App layout with header/footer
?   ?   ??? MeetingList.jsx  # Meeting table component
?   ?   ??? ProtectedRoute.jsx # Route protection wrapper
?   ??? context/             # React context providers
?   ?   ??? AuthContext.jsx  # Authentication state management
?   ??? pages/               # Page components
?   ?   ??? Login.jsx        # Login page
?   ?   ??? Dashboard.jsx    # Main dashboard with meeting list
?   ?   ??? AddMeeting.jsx   # Create meeting form
?   ?   ??? EditMeeting.jsx  # Edit meeting form
?   ??? services/            # API service layer
?   ?   ??? api.js           # Axios client with interceptors
?   ?   ??? authService.js   # Authentication API calls
?   ?   ??? meetingService.js # Meeting CRUD operations
?   ??? App.jsx              # Main app component with routing
?   ??? main.jsx             # Application entry point
?   ??? index.css            # Global styles
?   ??? App.css              # Component-specific styles
??? index.html               # HTML template
??? vite.config.js           # Vite configuration
??? package.json             # Dependencies and scripts
??? README.md               # This file
```

## API Endpoints Used

The application connects to the following Vaani.API endpoints:

- `POST /api/admin/login` - Admin authentication
- `GET /api/admin/adminmeetings` - Get all meetings
- `GET /api/admin/adminmeetings/{id}` - Get meeting by ID
- `POST /api/admin/adminmeetings` - Create new meeting
- `PUT /api/admin/adminmeetings/{id}` - Update meeting
- `DELETE /api/admin/adminmeetings/{id}` - Delete meeting

## Authentication

The application uses JWT tokens for authentication:

1. Login with your admin credentials
2. Token is stored in localStorage
3. Token is automatically included in all API requests
4. Token expiration redirects to login page

## Meeting Status

Meetings are automatically categorized:

- **Upcoming** - End date is in the future
- **Completed** - End date has passed

Only upcoming meetings can be edited or deleted. Completed meetings are read-only.

## Technology Stack

- **React 18** - UI library
- **Vite 5** - Build tool and dev server
- **React Router 6** - Client-side routing
- **Axios** - HTTP client
- **CSS3** - Styling with CSS variables

## Browser Support

- Chrome (latest)
- Firefox (latest)
- Safari (latest)
- Edge (latest)

## Troubleshooting

### API Connection Issues

If you encounter CORS or connection issues:

1. Ensure Vaani.API is running on `https://localhost:7020`
2. Check that CORS is enabled in the API
3. Verify the proxy configuration in `vite.config.js`

### Authentication Issues

If you can't login:

1. Verify admin credentials in the database
2. Check JWT configuration in Vaani.API
3. Clear localStorage: `localStorage.clear()`

### Build Issues

If npm install fails:

1. Delete `node_modules` and `package-lock.json`
2. Run `npm install` again
3. Try using a different Node.js version (18 LTS recommended)

## Development Notes

- Hot Module Replacement (HMR) is enabled for fast development
- ESLint is configured for code quality
- API requests are proxied through Vite dev server to avoid CORS issues

## License

Copyright © 2024 Vaani. All rights reserved.

## Support

For issues or questions, contact the Vaani development team.
