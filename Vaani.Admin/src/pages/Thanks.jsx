import { useEffect } from 'react'
import logo from '../Assets/logo.png'
import './Thanks.css'

function Thanks() {

    useEffect(() => {
        // Optional: Auto-close logic could go here if intended, 
        // but usually browsers block scripts from closing windows they didn't open.
    }, [])

    return (
        <div className="thanks-container">
            <div className="thanks-box">
                <div className="thanks-icon">
                    <img src={logo} height="50" alt="Vaani" className="logo-image" />
                </div>
                <p className="thanks-message">
                    Thank you for using Vaani. <br />
                    You can now safely close this window.
                </p>
            </div>
        </div>
    )
}

export default Thanks
