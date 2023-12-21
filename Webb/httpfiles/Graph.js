class Graph 
{
    constructor()
    {
        this.paths = [];

        this.height = 300;
        this.width = 600;
        this.padding_left = 30;
        this.padding_right = 100;
        this.bottom = this.height - 4;

        this.xlabel = [];
        this.ylabel = [];
        this.font =  "16px Roboto bold";
    }

    SetView(x, y, width, height)
    {
        this.view_x = x;
        this.view_y = y;
        this.view_width = width;
        this.view_height = height;
    }

    GetPath(name, color)
    {
        if (this.paths[name] == null)
        {
            this.paths[name] = new Path(name, color);
        }
        return this.paths[name];
    }

    AddXLabel(name, value)
    {
        this.xlabel[name] = value;
    }

    AddYLabel(name, value)
    {
        this.ylabel[name] = value;
    }

    MakeGraph(canvas)
    {
        var ctx = canvas.getContext("2d");
        ctx.font = this.font;

        for (const pathKey in this.paths)
        {
            const path = this.paths[pathKey];
            path.MakePath(ctx, this.width, this.height, this.view_x, this.view_y, this.view_width, this.view_height);
        }

        ctx.beginPath();
        ctx.strokeStyle = "white";
        ctx.lineWidth = 0.5;

        for (const name in this.xlabel)
        {
            let value = this.xlabel[name];
            let x = value - this.view_x;
            x = (x / this.view_width) * this.width;
            ctx.moveTo(x, 0);
            ctx.lineTo(x, this.bottom);

            ctx.fillStyle = "white";
            ctx.fillText(name, x, this.bottom);
        }

        for (const name in this.ylabel)
        {
            let value = this.ylabel[name];
            let y = value - this.view_y;
            y = (y / this.view_height) * this.height;
            ctx.moveTo(0, y);
            ctx.lineTo(this.width, y);

            ctx.fillStyle = "white";
            ctx.fillText(name, 0, y);
        }

        ctx.stroke();
    }
}

class Path
{
    constructor(name, color = "#FF0000")
    {
        this.values = [];
        this.color = color;
        this.name = name;
    }

    AddPoint(x, y)
    {
        this.values[x] = y;
    }

    MakePath(ctx, width, height, view_x, view_y, view_width, view_height)
    {
        ctx.beginPath();

        let dx = 0;
        let dy = 0;
        for (let x in this.values)
        {
            let y = this.values[x];

            dx = x - view_x;
            dx = (dx / view_width) * width;

            dy = y - view_y;
            dy = (dy / view_height) * height;

            ctx.lineTo(dx, dy);
        }

        ctx.strokeStyle = this.color;
        ctx.lineWidth = 3;
        ctx.stroke();

        ctx.fillStyle = this.color;
        ctx.fillText(this.name, dx, dy);
    }
}