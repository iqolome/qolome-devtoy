namespace Qolome.Nets.Core.Scanning;

public readonly record struct PortProfile(int Port, string Protocol, string Service, string Description);

public static class PortPurposeCatalog
{
    public const string Tcp = "TCP";
    public const string Udp = "UDP";

    private static readonly Dictionary<(int Port, string Protocol), PortProfile> Builtin = CreateBuiltin();

    public static string NormalizeProtocol(string? protocol) =>
        string.Equals(protocol, Udp, StringComparison.OrdinalIgnoreCase) ? Udp : Tcp;

    public static PortProfile? Find(int port, string? protocol)
    {
        var key = (port, NormalizeProtocol(protocol));
        return Builtin.TryGetValue(key, out var profile) ? profile : null;
    }

    public static string LookupService(int port, string? protocol = Tcp) =>
        Find(port, protocol)?.Service ?? "";

    private static Dictionary<(int, string), PortProfile> CreateBuiltin()
    {
        var map = new Dictionary<(int, string), PortProfile>();
        void Add(int port, string protocol, string service, string description) =>
            map[(port, protocol)] = new PortProfile(port, protocol, service, description);

        Add(20, Tcp, "FTP-DATA", "FTP 数据传输");
        Add(21, Tcp, "FTP", "文件传输");
        Add(22, Tcp, "SSH", "安全远程登录 / SFTP");
        Add(23, Tcp, "Telnet", "明文远程登录");
        Add(25, Tcp, "SMTP", "邮件发送");
        Add(53, Tcp, "DNS", "域名解析（TCP）");
        Add(53, Udp, "DNS", "域名解析");
        Add(67, Udp, "DHCP", "DHCP 服务端");
        Add(68, Udp, "DHCP", "DHCP 客户端");
        Add(69, Udp, "TFTP", "简单文件传输");
        Add(80, Tcp, "HTTP", "网页服务");
        Add(88, Tcp, "Kerberos", "域认证");
        Add(88, Udp, "Kerberos", "域认证");
        Add(110, Tcp, "POP3", "邮件接收");
        Add(111, Tcp, "RPCBind", "RPC 端口映射");
        Add(111, Udp, "RPCBind", "RPC 端口映射");
        Add(123, Udp, "NTP", "网络时间同步");
        Add(135, Tcp, "MSRPC", "Windows RPC 终结点映射");
        Add(137, Udp, "NetBIOS-NS", "NetBIOS 名称服务");
        Add(138, Udp, "NetBIOS-DGM", "NetBIOS 数据报");
        Add(139, Tcp, "NetBIOS-SSN", "NetBIOS 会话 / 老式文件共享");
        Add(143, Tcp, "IMAP", "邮件收取");
        Add(161, Udp, "SNMP", "网络设备管理");
        Add(162, Udp, "SNMP-Trap", "SNMP 告警");
        Add(179, Tcp, "BGP", "边界网关协议");
        Add(389, Tcp, "LDAP", "目录服务");
        Add(443, Tcp, "HTTPS", "加密网页服务");
        Add(445, Tcp, "SMB", "Windows 文件共享");
        Add(465, Tcp, "SMTPS", "加密邮件发送");
        Add(500, Udp, "ISAKMP", "IPsec / IKE 密钥交换");
        Add(514, Udp, "Syslog", "系统日志");
        Add(515, Tcp, "LPD", "打印后台");
        Add(520, Udp, "RIP", "路由信息协议");
        Add(548, Tcp, "AFP", "Apple 文件共享");
        Add(554, Tcp, "RTSP", "流媒体控制");
        Add(587, Tcp, "SMTP", "邮件提交");
        Add(631, Tcp, "IPP", "互联网打印协议");
        Add(636, Tcp, "LDAPS", "加密目录服务");
        Add(873, Tcp, "rsync", "文件同步");
        Add(989, Tcp, "FTPS", "FTP 数据（TLS）");
        Add(990, Tcp, "FTPS", "FTP 控制（TLS）");
        Add(993, Tcp, "IMAPS", "加密邮件收取");
        Add(995, Tcp, "POP3S", "加密邮件接收");
        Add(1080, Tcp, "SOCKS", "代理");
        Add(1194, Udp, "OpenVPN", "VPN");
        Add(1433, Tcp, "MSSQL", "SQL Server");
        Add(1434, Udp, "MSSQL-Monitor", "SQL Server 浏览器");
        Add(1521, Tcp, "Oracle", "Oracle 数据库");
        Add(1701, Udp, "L2TP", "L2TP VPN");
        Add(1723, Tcp, "PPTP", "PPTP VPN");
        Add(1812, Udp, "RADIUS", "认证计费");
        Add(1813, Udp, "RADIUS-Acct", "RADIUS 计费");
        Add(1883, Tcp, "MQTT", "物联网消息");
        Add(1900, Udp, "SSDP", "UPnP 设备发现");
        Add(2049, Tcp, "NFS", "网络文件系统");
        Add(2049, Udp, "NFS", "网络文件系统");
        Add(2082, Tcp, "cPanel", "主机面板");
        Add(2083, Tcp, "cPanel", "主机面板（TLS）");
        Add(2181, Tcp, "ZooKeeper", "分布式协调");
        Add(2375, Tcp, "Docker", "Docker API（明文）");
        Add(2376, Tcp, "Docker", "Docker API（TLS）");
        Add(2379, Tcp, "etcd", "集群配置存储");
        Add(2404, Tcp, "IEC-104", "电力远动");
        Add(3306, Tcp, "MySQL", "MySQL / MariaDB");
        Add(3389, Tcp, "RDP", "远程桌面");
        Add(3478, Udp, "STUN", "NAT 穿透");
        Add(3702, Udp, "WS-Discovery", "设备发现（ONVIF 等）");
        Add(4369, Tcp, "EPMD", "Erlang 端口映射");
        Add(4500, Udp, "IPsec-NAT-T", "IPsec NAT 穿越");
        Add(5000, Tcp, "UPnP/Synology", "UPnP 或群晖 DSM");
        Add(5060, Tcp, "SIP", "VoIP 信令");
        Add(5060, Udp, "SIP", "VoIP 信令");
        Add(5061, Tcp, "SIPS", "加密 VoIP 信令");
        Add(5353, Udp, "mDNS", "组播 DNS / Bonjour");
        Add(5355, Udp, "LLMNR", "链路本地名称解析");
        Add(5432, Tcp, "PostgreSQL", "PostgreSQL 数据库");
        Add(5672, Tcp, "AMQP", "消息队列（RabbitMQ）");
        Add(5900, Tcp, "VNC", "远程桌面");
        Add(5985, Tcp, "WinRM", "Windows 远程管理");
        Add(5986, Tcp, "WinRM", "Windows 远程管理（HTTPS）");
        Add(6379, Tcp, "Redis", "Redis 缓存");
        Add(6443, Tcp, "Kubernetes", "K8s API");
        Add(8000, Tcp, "HTTP", "备用 Web 端口");
        Add(8080, Tcp, "HTTP", "代理或备用 Web");
        Add(8081, Tcp, "HTTP", "备用 Web");
        Add(8443, Tcp, "HTTPS", "备用 HTTPS / 管理口");
        Add(8883, Tcp, "MQTT", "MQTT over TLS");
        Add(9000, Tcp, "HTTP", "常见应用端口");
        Add(9090, Tcp, "Prometheus", "监控或 Web 管理");
        Add(9100, Tcp, "JetDirect", "网络打印");
        Add(9200, Tcp, "Elasticsearch", "搜索引擎");
        Add(9443, Tcp, "HTTPS", "管理 HTTPS");
        Add(11211, Tcp, "Memcached", "缓存");
        Add(27017, Tcp, "MongoDB", "MongoDB 数据库");
        Add(32400, Tcp, "Plex", "媒体服务");
        Add(47808, Udp, "BACnet", "楼宇自控");
        return map;
    }
}
