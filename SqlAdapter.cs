using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot.Types;


public class SqlAdapter
{
    private DataTable dataTable;
    private MySqlConnection sqlConn;
    private MySqlDataAdapter sqlData;
    private MySqlCommandBuilder sqlBuilder;
    public int minimumHours;
    public SqlAdapter()
    {
        try
        {
            SqlAdapterConfig config = SqlAdapterConfig.GetConfiguration();
            sqlConn = new MySqlConnection(config.arg);
            minimumHours = config.minimumHours;
            sqlConn.Open();
            sqlData = new MySqlDataAdapter("SELECT * FROM TimeDate", sqlConn);
            dataTable = new DataTable();
            sqlData.Fill(dataTable);
            sqlBuilder = new MySqlCommandBuilder(sqlData);
            sqlBuilder.GetInsertCommand();
            sqlBuilder.GetUpdateCommand();
            sqlBuilder.GetDeleteCommand();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw new Exception("sql не подключен!!!!!");
        }
    }
    public SqlAdapter(string arg, int hours)
    {
        sqlConn = new MySqlConnection(arg);
        minimumHours = hours;
    }
    ~SqlAdapter()
    {
        sqlConn.Close();
    }
    /// <summary>
    /// Добавляет запись с прикреплёнными данными
    /// </summary>
    public void AddLog(long userId, DateTime dateTime)
    {
        try
        {
            if (dataTable == null)
                throw new Exception("dataTable is null!");

            DataRow row = dataTable.NewRow();
            row["UserID"] = GetHash(userId);
            row["MessageDate"] = dateTime.Ticks;
            dataTable.Rows.Add(row);
            sqlData.Update(dataTable);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
    /// <summary>
    /// Проверяет наличие записи пользователя с определённым userId
    /// </summary>
    /// <param name="userId"></param>
    /// <returns>Возвращает значение true, если запись с таким Id существует в таблице базы данных, иначе false</returns>
    public bool CheckLog(long userId)
    {
        try
        {
            if (dataTable == null)
                throw new Exception("dataTable is null!");

            byte[] fin = GetHash(userId);
            
            foreach (DataRow row in dataTable.Rows)
            {
                byte[] rowData = row["UserID"] as byte[];
                if (rowData.SequenceEqual(fin))
                {
                    return true;
                } 
            }
            return false;
        }
        catch (Exception ex) 
        {
            Console.WriteLine(ex.Message);
            return false;
        }
    }
    /// <summary>
    /// Удаляет строку определённого индекса из таблицы базы данных
    /// </summary>
    public void DeleteLog(int num)
    {
        try
        {
            dataTable.Rows[num].Delete();
            sqlData.Update(dataTable);
        }
        catch (Exception ex)
        {
            Console.WriteLine (ex.Message);
        }
    }
    /// <summary>
    /// Находит индекс, на котором находится нужная запись
    /// </summary>
    /// <param name="userId"></param>
    /// <returns> Индекс записи, иначе -1</returns>
    public int Find(long userId)
    {
        try
        {
            byte[] fin = GetHash(userId);

            for (int i = 0; i < dataTable.Rows.Count; i++)
            {
                byte[] rowData = dataTable.Rows[i]["UserID"] as byte[];
                if (rowData.SequenceEqual(fin))
                {
                    return i;
                }
            }
            return -1;
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex);
            return -1;
        }
    }
    /// <summary>
    /// Вычисляет, прошло ли достаточно времени с последней записи
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="time"></param>
    /// <param name="date"></param>
    /// <returns> 1, если прошло достаточное количество времени, 0 если недостаточно времени, -1 если предыдущей записи не существует </returns>
    public int CheckElapsedTime(long userId, DateTime dateTime)
    {
        int check = Find(userId);
        if (check == -1)
            return -1;
        long savedTime = dateTime.Ticks - Convert.ToInt64(dataTable.Rows[check]["MessageDate"]);
        TimeSpan elapsedTime = new TimeSpan(savedTime);
        if(elapsedTime.TotalHours > minimumHours)
        {
            return 1;
        }
        return 0;
    }
    static byte[] GetHash(long input)
    {
        byte[] data = BitConverter.GetBytes(input);
        byte[] result = new byte[64];

        using (SHA512 sha512 = SHA512.Create())
        {
            result = sha512.ComputeHash(data);
        }
        return result;
    }
}

public class SqlAdapterConfig
{
    public int minimumHours { get; set; }
    public string arg {  get; set; }
    public static SqlAdapterConfig GetConfiguration()
    {
        string filePath = "appsettings.json";

        var builder = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json");

        IConfigurationRoot configuration = builder.Build();

        var sql = new SqlAdapterConfig();
        var sqlAdapterConfigSection = configuration.GetSection("SqlAdapterConfig");
        sql.minimumHours = Convert.ToInt32(sqlAdapterConfigSection["minimumHours"]);
        sql.arg = sqlAdapterConfigSection["arg"];

        return sql;
    }
}
